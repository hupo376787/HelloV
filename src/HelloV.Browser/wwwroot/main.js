import { dotnet } from './_framework/dotnet.js';

if (typeof window === 'undefined') {
    throw new Error('HelloV Browser must run in a browser.');
}

const host = document.getElementById('out');
const splash = document.querySelector('.hellov-splash');
const splashStartedAt = performance.now();
const minimumSplashMilliseconds = 750;

async function waitForSplashImage() {
    const image = splash?.querySelector('img');
    if (!image) {
        return;
    }

    try {
        if (typeof image.decode === 'function') {
            await image.decode();
        } else if (!image.complete) {
            await new Promise(resolve => {
                image.addEventListener('load', resolve, { once: true });
                image.addEventListener('error', resolve, { once: true });
            });
        }
    } catch {
        // Keep startup moving even if the browser rejects decode().
    }
}

function waitForAvaloniaCanvas() {
    if (!host) {
        return Promise.resolve();
    }
    if (host.querySelector('canvas')) {
        return Promise.resolve();
    }

    return new Promise(resolve => {
        const observer = new MutationObserver(() => {
            if (host.querySelector('canvas')) {
                observer.disconnect();
                resolve();
            }
        });
        observer.observe(host, { childList: true, subtree: true });
    });
}

async function dismissSplashWhenReady() {
    if (!splash) {
        return;
    }

    await Promise.all([waitForSplashImage(), waitForAvaloniaCanvas()]);
    const elapsed = performance.now() - splashStartedAt;
    if (elapsed < minimumSplashMilliseconds) {
        await new Promise(resolve => setTimeout(resolve, minimumSplashMilliseconds - elapsed));
    }

    // Give Avalonia one complete paint opportunity after the canvas is inserted.
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    splash.classList.add('is-hiding');
    setTimeout(() => splash.remove(), 240);
}

const splashTask = dismissSplashWhenReady();

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

const config = dotnetRuntime.getConfig();
await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
await splashTask;
