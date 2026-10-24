package com.livelayer.app

/** Surface and visibility events can arrive in either order during preview -> home. */
internal class WallpaperEngineLifecycle(private val refresh: () -> Unit,
    private val startSensor: () -> Unit, private val stopSensor: () -> Unit,
    private val requestFrame: () -> Unit, private val cancelFrames: () -> Unit) {
    var visible = false; private set
    var surfaceReady = false; private set
    private var listening = false

    fun visibilityChanged(value: Boolean, surfaceAvailable: Boolean) {
        visible = value
        surfaceReady = surfaceAvailable
        reconcile()
    }

    fun surfaceCreated() { surfaceReady = true; reconcile() }
    fun surfaceDestroyed() { surfaceReady = false; reconcile() }
    fun selectionChanged() { if (surfaceReady) refresh(); if (visible && surfaceReady) requestFrame() }
    fun destroy() { visible = false; surfaceReady = false; reconcile() }

    private fun reconcile() {
        val active = visible && surfaceReady
        if (surfaceReady) refresh() // Read the committed selection even if a preference event was missed.
        if (active && !listening) { startSensor(); listening = true }
        else if (!active && listening) { stopSensor(); listening = false }
        if (active) requestFrame() else cancelFrames()
    }
}
