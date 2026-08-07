const INTERRUPT_MODE_KEY = 'hellov.interruptMode';

export function getInterruptMode() {
    const value = window.localStorage?.getItem(INTERRUPT_MODE_KEY);
    if (value === '1') {
        return 1;
    }
    if (value === '0') {
        return 0;
    }
    return -1;
}

export function setInterruptMode(enabled) {
    window.localStorage?.setItem(INTERRUPT_MODE_KEY, enabled ? '1' : '0');
}
