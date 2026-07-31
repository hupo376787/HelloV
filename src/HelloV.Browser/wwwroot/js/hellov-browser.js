const MODEL_INPUT_WIDTH = 640;
const MODEL_INPUT_HEIGHT = 640;
const MODEL_FILL = 114;
const MIN_BOX_AREA = 0.006;
const MAX_BOX_AREA = 0.90;
const DUPLICATE_IOU_THRESHOLD = 0.55;
const MAX_DETECTIONS = 8;
const INFERENCE_INTERVAL_MS = 50;

const video = document.getElementById('hellov-camera');
const previewCanvas = document.getElementById('hellov-preview');
const previewContext = previewCanvas?.getContext('2d', {
    alpha: false,
    desynchronized: true,
    willReadFrequently: true
});
const inferenceCanvas = document.getElementById('hellov-inference');
const inferenceContext = inferenceCanvas?.getContext('2d', {
    alpha: false,
    desynchronized: true,
    willReadFrequently: true
});

const emojiCanvas = document.createElement('canvas');
const emojiContext = emojiCanvas.getContext('2d', {
    alpha: true,
    willReadFrequently: true
});

let stream = null;
let session = null;
let ortRuntime = null;
let modelPromise = null;
let inputBuffer = null;
let inferenceBusy = false;
let lastInferenceTimestamp = 0;
let frameGeneration = 0;
let videoFrameCallbackId = null;
let animationFrameId = null;
let frameCounter = 0;
let frameCounterStarted = performance.now();
let mirrorPreview = true;

const runtimeState = {
    camera: {
        width: 0,
        height: 0,
        fps: 0
    },
    model: {
        state: 'idle',
        name: '',
        backend: '',
        error: '',
        loadSeconds: 0
    },
    detectionSequence: 0,
    detections: []
};

function ensureBrowserApis() {
    if (!navigator.mediaDevices?.getUserMedia || !navigator.mediaDevices?.enumerateDevices) {
        throw new Error('当前浏览器不支持 MediaDevices 摄像头 API。');
    }

    if (!video || !previewCanvas || !previewContext || !inferenceCanvas || !inferenceContext) {
        throw new Error('HelloV 浏览器摄像头元素初始化失败。');
    }
}

function inferFacing(label) {
    const text = (label || '').toLowerCase();
    if (/front|user|facetime|前置|自拍/.test(text)) {
        return 'front';
    }
    if (/back|rear|environment|后置|背面/.test(text)) {
        return 'back';
    }
    if (/external|usb|外接/.test(text)) {
        return 'external';
    }
    return 'unknown';
}

function cameraLabel(device, index) {
    const label = device.label?.trim();
    return label || `摄像头 ${index + 1}`;
}

export async function getCamerasJson() {
    ensureBrowserApis();

    // Device labels and stable device ids are generally hidden until camera permission is granted.
    let permissionStream = null;
    try {
        permissionStream = await navigator.mediaDevices.getUserMedia({
            audio: false,
            video: true
        });

        const devices = (await navigator.mediaDevices.enumerateDevices())
            .filter(device => device.kind === 'videoinput')
            .map((device, index) => ({
                id: device.deviceId || '',
                label: cameraLabel(device, index),
                facing: inferFacing(device.label)
            }));

        return JSON.stringify(devices);
    } finally {
        permissionStream?.getTracks().forEach(track => track.stop());
    }
}

export async function startCamera(deviceId, mirrorHorizontally) {
    ensureBrowserApis();
    await stopCamera();

    mirrorPreview = Boolean(mirrorHorizontally);
    applyMirror();

    const videoConstraints = {
        width: { ideal: 1920 },
        height: { ideal: 1080 },
        frameRate: { ideal: 30, max: 30 }
    };
    if (deviceId) {
        videoConstraints.deviceId = { exact: deviceId };
    }

    stream = await navigator.mediaDevices.getUserMedia({
        audio: false,
        video: videoConstraints
    });

    video.srcObject = stream;
    await video.play();

    const track = stream.getVideoTracks()[0];
    const settings = track?.getSettings?.() ?? {};
    runtimeState.camera.width = Number(settings.width || video.videoWidth || 0);
    runtimeState.camera.height = Number(settings.height || video.videoHeight || 0);
    runtimeState.camera.fps = 0;
    runtimeState.detections = [];
    runtimeState.detectionSequence++;

    frameCounter = 0;
    frameCounterStarted = performance.now();
    lastInferenceTimestamp = 0;
    inferenceBusy = false;

    const generation = ++frameGeneration;
    beginFrameLoop(generation);
}

export async function stopCamera() {
    frameGeneration++;

    if (videoFrameCallbackId !== null && video?.cancelVideoFrameCallback) {
        try {
            video.cancelVideoFrameCallback(videoFrameCallbackId);
        } catch {
            // Browser may have already invalidated the callback after a track ended.
        }
    }
    videoFrameCallbackId = null;

    if (animationFrameId !== null) {
        cancelAnimationFrame(animationFrameId);
    }
    animationFrameId = null;

    const previousStream = stream;
    stream = null;
    previousStream?.getTracks().forEach(track => track.stop());

    if (video) {
        video.pause();
        video.srcObject = null;
    }

    runtimeState.camera = { width: 0, height: 0, fps: 0 };
    runtimeState.detections = [];
    runtimeState.detectionSequence++;
    inferenceBusy = false;
}

export function toggleFullscreen() {
    if (document.fullscreenElement) {
        return document.exitFullscreen();
    }

    const target = document.documentElement;
    if (!target?.requestFullscreen) {
        return Promise.reject(new Error('当前浏览器不支持全屏 API。'));
    }

    return target.requestFullscreen({ navigationUI: 'hide' });
}

export function setMirror(mirrorHorizontally) {
    // The Avalonia CameraPreviewControl performs the visual mirror. This flag is also used when
    // mapping model detections so animation anchors stay aligned with the mirrored preview.
    mirrorPreview = Boolean(mirrorHorizontally);
}

function applyMirror() {
    // Retained for startCamera compatibility. The off-screen video itself is never transformed.
}

export function capturePreviewFrame(maxWidth, maxHeight) {
    if (!stream || !video || !previewCanvas || !previewContext ||
        video.readyState < HTMLMediaElement.HAVE_CURRENT_DATA) {
        return new Uint8Array(0);
    }

    const sourceWidth = Number(video.videoWidth || runtimeState.camera.width || 0);
    const sourceHeight = Number(video.videoHeight || runtimeState.camera.height || 0);
    if (sourceWidth <= 0 || sourceHeight <= 0) {
        return new Uint8Array(0);
    }

    const widthLimit = Math.max(2, Number(maxWidth) || sourceWidth);
    const heightLimit = Math.max(2, Number(maxHeight) || sourceHeight);
    const scale = Math.min(1, widthLimit / sourceWidth, heightLimit / sourceHeight);
    const targetWidth = Math.max(2, Math.round(sourceWidth * scale));
    const targetHeight = Math.max(2, Math.round(sourceHeight * scale));

    if (previewCanvas.width !== targetWidth || previewCanvas.height !== targetHeight) {
        previewCanvas.width = targetWidth;
        previewCanvas.height = targetHeight;
    }

    previewContext.setTransform(1, 0, 0, 1, 0, 0);
    previewContext.drawImage(video, 0, 0, targetWidth, targetHeight);
    const pixels = previewContext.getImageData(0, 0, targetWidth, targetHeight).data;

    // Header: width and height as little-endian Int32, followed by packed RGBA8888 bytes.
    const packet = new Uint8Array(8 + pixels.length);
    const header = new DataView(packet.buffer, 0, 8);
    header.setInt32(0, targetWidth, true);
    header.setInt32(4, targetHeight, true);
    packet.set(pixels, 8);
    return packet;
}

export function renderEmoji(emoji, pixelSize) {
    if (!emojiContext || !emoji) {
        return new Uint8Array(0);
    }

    const size = Math.max(16, Math.min(512, Math.round(Number(pixelSize) || 64)));
    emojiCanvas.width = size;
    emojiCanvas.height = size;

    emojiContext.setTransform(1, 0, 0, 1, 0, 0);
    emojiContext.clearRect(0, 0, size, size);
    emojiContext.textAlign = 'center';
    emojiContext.textBaseline = 'middle';
    emojiContext.font = `${Math.round(size * 0.78)}px "Segoe UI Emoji", "Apple Color Emoji", "Noto Color Emoji", sans-serif`;
    emojiContext.fillStyle = '#ffffff';
    emojiContext.fillText(String(emoji), size / 2, size / 2 + size * 0.025);

    const pixels = emojiContext.getImageData(0, 0, size, size).data;
    const packet = new Uint8Array(8 + pixels.length);
    const header = new DataView(packet.buffer, 0, 8);
    header.setInt32(0, size, true);
    header.setInt32(4, size, true);
    packet.set(pixels, 8);
    return packet;
}

export async function initializeModel(preferredModelUrl, fallbackModelUrl) {
    if (!modelPromise) {
        modelPromise = loadModel(preferredModelUrl, fallbackModelUrl);
    }

    await modelPromise;
    return JSON.stringify(runtimeState.model);
}

export async function initializeModelBytes(modelBytes, modelName) {
    if (!modelPromise) {
        modelPromise = loadModelBytes(modelBytes, modelName);
    }

    await modelPromise;
    return JSON.stringify(runtimeState.model);
}

async function loadModelBytes(modelBytes, modelName) {
    const started = performance.now();
    runtimeState.model = {
        state: 'loading',
        name: '',
        backend: '',
        error: '',
        loadSeconds: 0
    };

    try {
        configureOnnxRuntime();
        const bytes = modelBytes instanceof Uint8Array
            ? modelBytes
            : new Uint8Array(modelBytes || []);
        const validationError = validateOnnxPayload(bytes, 'application/octet-stream');
        if (validationError) {
            throw new Error(validationError);
        }

        const backend = await createInferenceSession(bytes);
        inputBuffer = new Float32Array(3 * MODEL_INPUT_WIDTH * MODEL_INPUT_HEIGHT);
        runtimeState.model = {
            state: 'ready',
            name: modelName || 'embedded.onnx',
            backend,
            error: '',
            loadSeconds: elapsedSeconds(started)
        };
    } catch (error) {
        session = null;
        inputBuffer = null;
        runtimeState.model = {
            state: 'error',
            name: modelName || '',
            backend: '',
            error: messageOf(error),
            loadSeconds: elapsedSeconds(started)
        };
    }
}

async function loadModel(preferredModelUrl, fallbackModelUrl) {
    const started = performance.now();
    runtimeState.model = {
        state: 'loading',
        name: '',
        backend: '',
        error: '',
        loadSeconds: 0
    };

    try {
        configureOnnxRuntime();

        const candidates = buildModelUrlCandidates(preferredModelUrl, fallbackModelUrl);

        let modelBytes = null;
        let modelUrl = '';
        const fetchErrors = [];
        for (const candidate of candidates) {
            try {
                const response = await fetch(candidate, { cache: 'no-store' });
                if (!response.ok) {
                    fetchErrors.push(`${candidate}: HTTP ${response.status}`);
                    continue;
                }

                const contentType = (response.headers.get('content-type') || '').toLowerCase();
                const candidateBytes = new Uint8Array(await response.arrayBuffer());
                const validationError = validateOnnxPayload(candidateBytes, contentType);
                if (validationError) {
                    fetchErrors.push(`${candidate}: ${validationError}`);
                    continue;
                }

                modelBytes = candidateBytes;
                modelUrl = candidate;
                break;
            } catch (error) {
                fetchErrors.push(`${candidate}: ${messageOf(error)}`);
            }
        }

        if (!modelBytes) {
            runtimeState.model = {
                state: 'missing',
                name: '',
                backend: '',
                error: (fetchErrors.join('；') || '没有找到 ONNX 模型。') +
                    ' 请把模型放到 src/HelloV.Browser/wwwroot/models 或项目根目录 Models；不要只复制到 bin/obj。',
                loadSeconds: elapsedSeconds(started)
            };
            return;
        }

        const backend = await createInferenceSession(modelBytes);

        inputBuffer = new Float32Array(3 * MODEL_INPUT_WIDTH * MODEL_INPUT_HEIGHT);
        runtimeState.model = {
            state: 'ready',
            name: basename(modelUrl),
            backend,
            error: '',
            loadSeconds: elapsedSeconds(started)
        };
    } catch (error) {
        session = null;
        inputBuffer = null;
        runtimeState.model = {
            state: 'error',
            name: '',
            backend: '',
            error: messageOf(error),
            loadSeconds: elapsedSeconds(started)
        };
    }
}

export function getRuntimeStateJson() {
    return JSON.stringify(runtimeState);
}

function beginFrameLoop(generation) {
    const onFrame = (timestamp, metadata) => {
        if (generation !== frameGeneration || !stream) {
            return;
        }

        const width = Number(metadata?.width || video.videoWidth || runtimeState.camera.width || 0);
        const height = Number(metadata?.height || video.videoHeight || runtimeState.camera.height || 0);
        if (width > 0 && height > 0) {
            runtimeState.camera.width = width;
            runtimeState.camera.height = height;
        }

        updateFps(timestamp);
        maybeRunInference(timestamp);

        videoFrameCallbackId = video.requestVideoFrameCallback(onFrame);
    };

    if (typeof video.requestVideoFrameCallback === 'function') {
        videoFrameCallbackId = video.requestVideoFrameCallback(onFrame);
        return;
    }

    const onAnimationFrame = timestamp => {
        if (generation !== frameGeneration || !stream) {
            return;
        }

        runtimeState.camera.width = Number(video.videoWidth || runtimeState.camera.width || 0);
        runtimeState.camera.height = Number(video.videoHeight || runtimeState.camera.height || 0);
        updateFps(timestamp);
        maybeRunInference(timestamp);
        animationFrameId = requestAnimationFrame(onAnimationFrame);
    };
    animationFrameId = requestAnimationFrame(onAnimationFrame);
}

function updateFps(timestamp) {
    frameCounter++;
    const elapsed = timestamp - frameCounterStarted;
    if (elapsed < 1000) {
        return;
    }

    runtimeState.camera.fps = frameCounter * 1000 / elapsed;
    frameCounter = 0;
    frameCounterStarted = timestamp;
}

function maybeRunInference(timestamp) {
    if (!session || !inputBuffer || inferenceBusy || video.readyState < HTMLMediaElement.HAVE_CURRENT_DATA) {
        return;
    }
    if (timestamp - lastInferenceTimestamp < INFERENCE_INTERVAL_MS) {
        return;
    }

    lastInferenceTimestamp = timestamp;
    inferenceBusy = true;
    void inferCurrentFrame()
        .catch(error => {
            console.warn('HelloV browser inference failed.', error);
            runtimeState.detections = [];
            runtimeState.detectionSequence++;
        })
        .finally(() => {
            inferenceBusy = false;
        });
}

async function inferCurrentFrame() {
    const sourceWidth = video.videoWidth;
    const sourceHeight = video.videoHeight;
    if (sourceWidth <= 0 || sourceHeight <= 0) {
        runtimeState.detections = [];
        runtimeState.detectionSequence++;
        return;
    }

    const letterbox = fillInputTensor(sourceWidth, sourceHeight);
    const inputName = session.inputNames[0];
    const tensor = new ortRuntime.Tensor(
        'float32',
        inputBuffer,
        [1, 3, MODEL_INPUT_HEIGHT, MODEL_INPUT_WIDTH]);
    const outputMap = await session.run({ [inputName]: tensor });
    const outputName = session.outputNames[0];
    const output = outputMap[outputName] ?? Object.values(outputMap)[0];
    if (!output?.data || !output?.dims) {
        throw new Error('YOLOv10 模型没有返回有效输出。');
    }

    runtimeState.detections = parseDetections(
        output.data,
        output.dims,
        letterbox,
        sourceWidth,
        sourceHeight);
    runtimeState.detectionSequence++;
}

function fillInputTensor(sourceWidth, sourceHeight) {
    inferenceContext.save();
    inferenceContext.setTransform(1, 0, 0, 1, 0, 0);
    inferenceContext.fillStyle = `rgb(${MODEL_FILL}, ${MODEL_FILL}, ${MODEL_FILL})`;
    inferenceContext.fillRect(0, 0, MODEL_INPUT_WIDTH, MODEL_INPUT_HEIGHT);

    const scale = Math.min(
        MODEL_INPUT_WIDTH / sourceWidth,
        MODEL_INPUT_HEIGHT / sourceHeight);
    const drawWidth = sourceWidth * scale;
    const drawHeight = sourceHeight * scale;
    const padX = (MODEL_INPUT_WIDTH - drawWidth) / 2;
    const padY = (MODEL_INPUT_HEIGHT - drawHeight) / 2;
    inferenceContext.drawImage(video, padX, padY, drawWidth, drawHeight);
    inferenceContext.restore();

    const rgba = inferenceContext.getImageData(
        0,
        0,
        MODEL_INPUT_WIDTH,
        MODEL_INPUT_HEIGHT).data;
    const planeSize = MODEL_INPUT_WIDTH * MODEL_INPUT_HEIGHT;
    for (let pixel = 0, source = 0; pixel < planeSize; pixel++, source += 4) {
        inputBuffer[pixel] = rgba[source] / 255;
        inputBuffer[planeSize + pixel] = rgba[source + 1] / 255;
        inputBuffer[2 * planeSize + pixel] = rgba[source + 2] / 255;
    }

    return { scale, padX, padY };
}

function parseDetections(values, dimensions, letterbox, sourceWidth, sourceHeight) {
    const shape = resolveOutputShape(dimensions, values.length);
    if (!shape) {
        throw new Error(`不支持的 YOLOv10 输出形状：[${dimensions.join(', ')}]。`);
    }

    const candidates = [];
    for (let row = 0; row < shape.rows; row++) {
        const confidence = readOutput(values, row, 4, shape);
        const classId = Math.round(readOutput(values, row, 5, shape));
        const kind = classId + 1;
        if (kind <= 0 || kind > 33 || confidence < confidenceThreshold(kind)) {
            continue;
        }

        let x1 = readOutput(values, row, 0, shape);
        let y1 = readOutput(values, row, 1, shape);
        let x2 = readOutput(values, row, 2, shape);
        let y2 = readOutput(values, row, 3, shape);

        // Some exports use normalized model coordinates; standard YOLOv10 end-to-end exports use pixels.
        const maxCoordinate = Math.max(Math.abs(x1), Math.abs(y1), Math.abs(x2), Math.abs(y2));
        if (maxCoordinate <= 2) {
            x1 *= MODEL_INPUT_WIDTH;
            x2 *= MODEL_INPUT_WIDTH;
            y1 *= MODEL_INPUT_HEIGHT;
            y2 *= MODEL_INPUT_HEIGHT;
        }

        const sourceRect = modelRectToSourceRect(
            x1,
            y1,
            x2,
            y2,
            letterbox,
            sourceWidth,
            sourceHeight);
        if (!sourceRect || sourceRect.width * sourceRect.height < MIN_BOX_AREA ||
            sourceRect.width * sourceRect.height > MAX_BOX_AREA) {
            continue;
        }

        candidates.push({
            kind,
            confidence,
            ...sourceRect
        });
    }

    candidates.sort((left, right) => right.confidence - left.confidence);
    const kept = [];
    for (const candidate of candidates) {
        if (kept.some(existing => existing.kind === candidate.kind && iou(existing, candidate) > DUPLICATE_IOU_THRESHOLD)) {
            continue;
        }

        kept.push(mapSourceRectToViewport(candidate, sourceWidth, sourceHeight));
        if (kept.length >= MAX_DETECTIONS) {
            break;
        }
    }

    return kept;
}

function resolveOutputShape(dimensions, valueCount) {
    if (!Array.isArray(dimensions) || dimensions.length < 2 || valueCount < 6) {
        return null;
    }

    if (Number(dimensions[dimensions.length - 1]) === 6) {
        return { rows: Math.floor(valueCount / 6), transposed: false };
    }

    if (Number(dimensions[dimensions.length - 2]) === 6) {
        const rows = Number(dimensions[dimensions.length - 1]);
        return rows > 0 && rows * 6 <= valueCount
            ? { rows, transposed: true }
            : null;
    }

    return null;
}

function readOutput(values, row, field, shape) {
    return Number(shape.transposed
        ? values[field * shape.rows + row]
        : values[row * 6 + field]);
}

function modelRectToSourceRect(x1, y1, x2, y2, letterbox, sourceWidth, sourceHeight) {
    let left = (Math.min(x1, x2) - letterbox.padX) / letterbox.scale;
    let top = (Math.min(y1, y2) - letterbox.padY) / letterbox.scale;
    let right = (Math.max(x1, x2) - letterbox.padX) / letterbox.scale;
    let bottom = (Math.max(y1, y2) - letterbox.padY) / letterbox.scale;

    left = clamp(left, 0, sourceWidth);
    top = clamp(top, 0, sourceHeight);
    right = clamp(right, 0, sourceWidth);
    bottom = clamp(bottom, 0, sourceHeight);
    if (right <= left || bottom <= top) {
        return null;
    }

    return {
        x: left / sourceWidth,
        y: top / sourceHeight,
        width: (right - left) / sourceWidth,
        height: (bottom - top) / sourceHeight
    };
}

function mapSourceRectToViewport(sourceRect, sourceWidth, sourceHeight) {
    const viewportWidth = Math.max(1, window.innerWidth || document.documentElement.clientWidth || 1);
    const viewportHeight = Math.max(1, window.innerHeight || document.documentElement.clientHeight || 1);
    const coverScale = Math.max(viewportWidth / sourceWidth, viewportHeight / sourceHeight);
    const displayedWidth = sourceWidth * coverScale;
    const displayedHeight = sourceHeight * coverScale;
    const offsetX = (viewportWidth - displayedWidth) / 2;
    const offsetY = (viewportHeight - displayedHeight) / 2;

    let x = (sourceRect.x * sourceWidth * coverScale + offsetX) / viewportWidth;
    const y = (sourceRect.y * sourceHeight * coverScale + offsetY) / viewportHeight;
    const width = sourceRect.width * sourceWidth * coverScale / viewportWidth;
    const height = sourceRect.height * sourceHeight * coverScale / viewportHeight;

    if (mirrorPreview) {
        x = 1 - x - width;
    }

    const left = clamp(x, 0, 1);
    const top = clamp(y, 0, 1);
    const right = clamp(x + width, 0, 1);
    const bottom = clamp(y + height, 0, 1);
    return {
        kind: sourceRect.kind,
        confidence: sourceRect.confidence,
        x: Math.min(left, right),
        y: Math.min(top, bottom),
        width: Math.abs(right - left),
        height: Math.abs(bottom - top)
    };
}

function confidenceThreshold(kind) {
    switch (kind) {
        case 9:  // HandHeart
        case 10: // HandHeart2
            return 0.38;
        case 14: // Dislike
        case 17: // Like
            return 0.48;
        case 22: // Peace
        case 23: // PeaceInverted
        case 24: // Rock
            return 0.46;
        case 11: // LittleFinger
        case 12: // MiddleFinger
        case 32: // ThumbIndex
            return 0.50;
        case 8:  // XSign
        case 13: // TakePicture
        case 33: // ThumbIndex2
            return 0.42;
        default:
            return 0.44;
    }
}

function iou(left, right) {
    const x1 = Math.max(left.x, right.x);
    const y1 = Math.max(left.y, right.y);
    const x2 = Math.min(left.x + left.width, right.x + right.width);
    const y2 = Math.min(left.y + left.height, right.y + right.height);
    const intersection = Math.max(0, x2 - x1) * Math.max(0, y2 - y1);
    const union = left.width * left.height + right.width * right.height - intersection;
    return union > 0 ? intersection / union : 0;
}

function clamp(value, minimum, maximum) {
    return Math.min(maximum, Math.max(minimum, value));
}


function configureOnnxRuntime() {
    ortRuntime = globalThis.ort;
    if (!ortRuntime?.InferenceSession || !ortRuntime?.Tensor) {
        throw new Error('ONNX Runtime Web 脚本没有加载。');
    }

    // A single WASM worker works without COOP/COEP headers and leaves CPU time for Avalonia.
    ortRuntime.env.wasm.wasmPaths = 'https://cdn.jsdelivr.net/npm/onnxruntime-web@1.27.0/dist/';
    ortRuntime.env.wasm.numThreads = 1;
}

async function createInferenceSession(modelBytes) {
    session = null;
    let backend = 'WASM';
    if (navigator.gpu) {
        try {
            session = await ortRuntime.InferenceSession.create(modelBytes, {
                executionProviders: ['webgpu', 'wasm'],
                graphOptimizationLevel: 'all'
            });
            backend = 'WebGPU';
        } catch (webGpuError) {
            console.warn('HelloV WebGPU initialization failed; falling back to WASM.', webGpuError);
            session = null;
        }
    }

    if (!session) {
        session = await ortRuntime.InferenceSession.create(modelBytes, {
            executionProviders: ['wasm'],
            graphOptimizationLevel: 'all'
        });
        backend = 'WASM';
    }

    return backend;
}

function buildModelUrlCandidates(...values) {
    const result = [];
    const add = value => {
        if (!value) return;
        try {
            const url = new URL(String(value), document.baseURI).href;
            if (!result.includes(url)) result.push(url);
        } catch {
            if (!result.includes(String(value))) result.push(String(value));
        }
    };

    for (const value of values) {
        add(value);
        const fileName = basename(value);
        if (fileName) {
            add(`models/${fileName}`);
            add(`/models/${fileName}`);
            add(`Models/${fileName}`);
            add(`/Models/${fileName}`);
        }
    }

    return result;
}

function validateOnnxPayload(bytes, contentType) {
    if (!(bytes instanceof Uint8Array) || bytes.length < 1024) {
        return `模型文件过小（${bytes?.length || 0} 字节），请先运行 scripts/prepare-hagridv2-model.ps1 重新导出。`;
    }

    const prefixLength = Math.min(bytes.length, 512);
    let prefix = '';
    try {
        prefix = new TextDecoder('utf-8', { fatal: false })
            .decode(bytes.subarray(0, prefixLength))
            .trimStart()
            .toLowerCase();
    } catch {
        prefix = '';
    }

    if (prefix.startsWith('version https://git-lfs.github.com/spec/v1')) {
        return '读取到的是 Git LFS 指针，不是真正的 ONNX 模型。请拉取 LFS 文件或重新导出模型。';
    }
    if (prefix.startsWith('<!doctype html') || prefix.startsWith('<html') ||
        contentType.includes('text/html')) {
        return '服务器返回了 HTML 页面，不是 ONNX 模型。';
    }
    if (prefix.startsWith('{') && (contentType.includes('json') || prefix.includes('error'))) {
        return '服务器返回了 JSON 错误内容，不是 ONNX 模型。';
    }
    if (contentType.startsWith('text/')) {
        return `服务器返回了 ${contentType}，不是二进制 ONNX 模型。`;
    }

    return '';
}

function basename(url) {
    const normalized = String(url || '').split(/[?#]/, 1)[0];
    return normalized.substring(normalized.lastIndexOf('/') + 1) || normalized;
}

function elapsedSeconds(started) {
    return Math.max(0, (performance.now() - started) / 1000);
}

function messageOf(error) {
    if (error instanceof Error) {
        return error.message;
    }
    return String(error ?? '未知错误');
}
