package com.livelayer.app

import android.content.SharedPreferences
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.app.ActivityManager
import android.app.KeyguardManager
import android.app.WallpaperManager
import android.app.wallpaper.WallpaperDescription
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.RectF
import android.graphics.Paint
import android.os.Handler
import android.os.Looper
import android.os.Build
import android.os.PersistableBundle
import androidx.annotation.RequiresApi
import androidx.core.content.ContextCompat
import android.service.wallpaper.WallpaperService
import android.util.Log
import android.view.SurfaceHolder
import android.view.WindowManager
import android.view.Choreographer
import org.json.JSONObject
import java.io.File
import kotlin.math.max

class LiveLayerWallpaperService : WallpaperService() {
    private val maxDecodedSide by lazy {
        if ((getSystemService(ACTIVITY_SERVICE) as ActivityManager).isLowRamDevice) 1024 else 1600
    }
    override fun onCreateEngine(): Engine = LiveLayerEngine()

    @RequiresApi(36)
    override fun onCreateEngine(description: WallpaperDescription): Engine =
        LiveLayerEngine(description.content.getString("manifest_path"), description.content.getInt("manifest_target"))

    private data class Layer(val bitmap: Bitmap, val order: Int, val positionX: Float,
        val positionY: Float, val rotation: Float, val scale: Float, val movementX: Float, val movementY: Float, val background: Boolean = false,
        val role: String = "Other", val layerId: String = "") {
        var cachedRigPart: String? = null
    }

    private inner class LiveLayerEngine(private val describedManifest: String? = null,
        private val describedTarget: Int = 0) : Engine(), SharedPreferences.OnSharedPreferenceChangeListener {
        private val handler = Handler(Looper.getMainLooper())
        private val preferences = getSharedPreferences("live_layer", MODE_PRIVATE)
        private val paint = Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG)
        private var loadedManifest: String? = null
        private var previewManifest: String? = null
        private var previewTarget = 0
        private var destinations = 0
        private var renderingTarget = WallpaperManager.FLAG_SYSTEM
        private val keyguard by lazy { getSystemService(KEYGUARD_SERVICE) as KeyguardManager }
        private val keyguardProbe = object : Runnable {
            override fun run() {
                // SCREEN_ON can arrive before Keyguard changes state on a shared engine.
                if (currentTarget() != renderingTarget) lifecycle.selectionChanged()
                handler.postDelayed(this, 250)
            }
        }
        private val keyguardChanges = object : BroadcastReceiver() {
            override fun onReceive(context: Context, intent: Intent) {
                refreshDestinations()
                lifecycle.selectionChanged()
                watchSharedKeyguard()
            }
        }
        private var tiltX = 0f
        private var tiltY = 0f
        private var targetTiltX = 0f
        private var targetTiltY = 0f
        private var lastFrameNanos = 0L
        private var motionEnabled = true
        private val choreographer = Choreographer.getInstance()
        private var sensitivity = 1f
        private var live2D = false
        private var rig = JSONObject()
        private var agent = false
        private var owner = ""
        private var canvasWidth = 1f
        private var canvasHeight = 1f
        private var decodeSide = maxDecodedSide
        private val layerRect = RectF()
        private fun authorized() = !agent || LiveActivationPolicy.mayAnimate(preferences.getBoolean("companion_live_enabled", false),
            preferences.getString("companion_owner", "") == owner, preferences.getLong("companion_valid_until", 0), System.currentTimeMillis())
        private val leaseCheck = object : Runnable {
            override fun run() {
                if (agent && !authorized()) { sensor.stop(); tiltX = 0f; tiltY = 0f; scheduleDraw() }
                handler.postDelayed(this, 15000)
            }
        }
        private var layers: List<Layer> = emptyList()
        private var fallback: Bitmap? = null
        private var frameScheduled = false
        private val frame = object : Choreographer.FrameCallback {
            override fun doFrame(frameTimeNanos: Long) {
                frameScheduled = false
                if (!lifecycle.visible || !lifecycle.surfaceReady) return
                val elapsed = if (lastFrameNanos == 0L) 1.0 / 60 else
                    ((frameTimeNanos - lastFrameNanos) / 1_000_000_000.0).coerceIn(0.0, .1)
                lastFrameNanos = frameTimeNanos
                // Final frame interpolation runs independently of irregular sensor events.
                val amount = (1 - kotlin.math.exp(-elapsed / .055)).toFloat()
                val canAnimate = motionEnabled && authorized()
                if (canAnimate) {
                    tiltX += (targetTiltX - tiltX) * amount
                    tiltY += (targetTiltY - tiltY) * amount
                } else { tiltX = 0f; tiltY = 0f }
                draw(canAnimate)
                // A still wallpaper needs no continuous GPU redraw. The sensor
                // callback below wakes rendering when the device moves again.
                if (WallpaperFrameGate.needsAnotherFrame(canAnimate, layers.isNotEmpty(),
                        tiltX, tiltY, targetTiltX, targetTiltY)) {
                    scheduleDraw()
                } else {
                    // A new gesture should start with a fresh frame delta, not
                    // the elapsed seconds since the last stationary frame.
                    lastFrameNanos = 0L
                }
            }
        }
        private val sensor = TiltSensor(this@LiveLayerWallpaperService, {
            if (Build.VERSION.SDK_INT >= 30) displayContext?.display?.rotation ?: 0
            else (getSystemService(WINDOW_SERVICE) as WindowManager).defaultDisplay.rotation
        }) { x, y ->
            if (WallpaperFrameGate.targetChanged(targetTiltX, targetTiltY, x, y)) {
                targetTiltX = x; targetTiltY = y
                scheduleDraw()
            }
        }
        private val lifecycle: WallpaperEngineLifecycle = WallpaperEngineLifecycle(
            refresh = { loadActiveProject(surfaceHolder.surfaceFrame.width(), surfaceHolder.surfaceFrame.height()) },
            startSensor = { if (authorized() && motionEnabled) sensor.start() }, stopSensor = { sensor.stop() },
            requestFrame = { scheduleDraw() }, cancelFrames = {
                choreographer.removeFrameCallback(frame); frameScheduled = false; lastFrameNanos = 0L
            })

        private fun currentTarget() = LiveWallpaperDestinations.frameTarget(destinations,
            destinations == LiveWallpaperDestinations.BOTH && keyguard.isKeyguardLocked)

        private fun refreshDestinations(flags: Int = if (Build.VERSION.SDK_INT >= 34) wallpaperFlags else WallpaperManager.FLAG_SYSTEM) {
            if (isPreview) return
            val lockId = if (flags == WallpaperManager.FLAG_SYSTEM) runCatching {
                WallpaperManager.getInstance(this@LiveLayerWallpaperService).getWallpaperId(WallpaperManager.FLAG_LOCK)
            }.getOrNull() else null
            val updated = LiveWallpaperDestinations.withInheritedLock(flags, lockId)
            if (updated == destinations) return
            destinations = updated
            CompanionLiveEngines.attach(this, updated)
            CompanionRuntime.pollSoon()
        }

        private fun watchSharedKeyguard() {
            handler.removeCallbacks(keyguardProbe)
            if (destinations == LiveWallpaperDestinations.BOTH && lifecycle.visible && lifecycle.surfaceReady)
                handler.postDelayed(keyguardProbe, 250)
        }

        override fun onCreate(holder: SurfaceHolder) {
            super.onCreate(holder)
            if (isPreview) {
                previewManifest = preferences.getString("preview_manifest_path", null)
                previewTarget = preferences.getInt("preview_manifest_target", 0)
            }
            else {
                refreshDestinations()
                ContextCompat.registerReceiver(this@LiveLayerWallpaperService, keyguardChanges, IntentFilter().apply {
                    addAction(Intent.ACTION_SCREEN_ON)
                    addAction(Intent.ACTION_SCREEN_OFF)
                    addAction(Intent.ACTION_USER_PRESENT)
                    addAction(Intent.ACTION_WALLPAPER_CHANGED)
                }, ContextCompat.RECEIVER_NOT_EXPORTED)
            }
            preferences.registerOnSharedPreferenceChangeListener(this)
            handler.postDelayed(leaseCheck, 15000)
        }

        override fun onSharedPreferenceChanged(preferences: SharedPreferences?, key: String?) {
            if (key == "companion_manifest_revision" && agent) {
                loadedManifest = null; lifecycle.selectionChanged()
            }
            if ((key == "companion_valid_until" || key == "companion_live_enabled") && agent) {
                if (authorized() && lifecycle.visible && motionEnabled) sensor.start() else { sensor.stop(); tiltX = 0f; tiltY = 0f }
                scheduleDraw()
            }
            if ((key == "manifest_path" || key == "direct_manifest_path" ||
                    key == CompanionLiveEngines.selectionKey(WallpaperManager.FLAG_SYSTEM) ||
                    key == CompanionLiveEngines.selectionKey(WallpaperManager.FLAG_LOCK)) && !isPreview) {
                refreshDestinations()
                lifecycle.selectionChanged()
                watchSharedKeyguard()
            }
        }

        @RequiresApi(34)
        override fun onWallpaperFlagsChanged(which: Int) {
            super.onWallpaperFlagsChanged(which)
            refreshDestinations(which)
            lifecycle.selectionChanged()
            watchSharedKeyguard()
        }

        override fun onVisibilityChanged(isVisible: Boolean) {
            refreshDestinations()
            lifecycle.visibilityChanged(isVisible, surfaceHolder.surface.isValid)
            watchSharedKeyguard()
        }

        override fun onSurfaceCreated(holder: SurfaceHolder) {
            super.onSurfaceCreated(holder)
            if (Build.VERSION.SDK_INT >= 30) {
                // Hint the compositor to avoid choosing a 30Hz mode while this wallpaper moves.
                val rate = (displayContext?.display?.refreshRate ?: 60f).coerceAtLeast(60f)
                runCatching { holder.surface.setFrameRate(rate, android.view.Surface.FRAME_RATE_COMPATIBILITY_DEFAULT) }
            }
            refreshDestinations()
            lifecycle.surfaceCreated()
            watchSharedKeyguard()
        }

        override fun onSurfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) {
            super.onSurfaceChanged(holder, format, width, height)
            refreshDestinations()
            lifecycle.surfaceCreated()
            watchSharedKeyguard()
            scheduleDraw()
        }

        override fun onSurfaceDestroyed(holder: SurfaceHolder) {
            lifecycle.surfaceDestroyed()
            watchSharedKeyguard()
            super.onSurfaceDestroyed(holder)
        }

        override fun onDestroy() {
            lifecycle.destroy()
            watchSharedKeyguard()
            if (!isPreview) {
                CompanionLiveEngines.detach(this)
                unregisterReceiver(keyguardChanges)
            }
            handler.removeCallbacks(leaseCheck)
            preferences.unregisterOnSharedPreferenceChangeListener(this)
            recycle()
            super.onDestroy()
        }

        @RequiresApi(36)
        override fun onApplyWallpaper(which: Int): WallpaperDescription? {
            val path = previewManifest ?: loadedManifest ?: return null
            if (!LiveWallpaperDestinations.choiceIncludes(previewTarget, which)) return null
            val content = PersistableBundle().apply {
                putString("manifest_path", path)
                putInt("manifest_target", previewTarget)
            }
            return WallpaperDescription.Builder().setId(File(path).parentFile?.name ?: path)
                .setContent(content).build()
        }

        private fun recycle() {
            layers.forEach { it.bitmap.recycle() }; layers = emptyList()
            fallback?.recycle(); fallback = null
        }

        private fun loadActiveProject(width: Int, height: Int) {
            if (width <= 0 || height <= 0) return
            renderingTarget = if (isPreview) WallpaperManager.FLAG_SYSTEM else currentTarget()
            val base = (if (isPreview) previewManifest else describedManifest?.takeIf { describedTarget == 0 })
                ?: preferences.getString("manifest_path", null)
            val direct = if (isPreview) null else preferences.getString("direct_manifest_path", null)
            fun revision(path: String?): Long = path?.let {
                runCatching { JSONObject(File(it).readText()).optLong("selectionRevision", 0) }.getOrDefault(0)
            } ?: 0
            val selection = if (isPreview) null else {
                preferences.getString(CompanionLiveEngines.selectionKey(renderingTarget), null)
                    ?: describedManifest?.takeIf { describedTarget == renderingTarget }
                    ?: if (renderingTarget == WallpaperManager.FLAG_LOCK && destinations == LiveWallpaperDestinations.BOTH)
                        preferences.getString(CompanionLiveEngines.selectionKey(WallpaperManager.FLAG_SYSTEM), null)
                            ?: describedManifest?.takeIf { describedTarget == WallpaperManager.FLAG_SYSTEM }
                    else null
            }
            val path = selection ?: LiveWallpaperSelection.choose(base, revision(base), direct, revision(direct)) ?: return
            if (loadedManifest == path) return
            recycle()
            loadedManifest = path // Retry failed content only when a new snapshot is selected.
            val loaded = mutableListOf<Layer>()
            try {
                val file = File(path)
                val directory = file.parentFile ?: return
                val manifest = JSONObject(file.readText())
                agent = manifest.optString("protocol") == "agent-v1"
                owner = manifest.optString("ownerId")
                canvasWidth = manifest.optInt("width", 1).toFloat().coerceAtLeast(1f)
                canvasHeight = manifest.optInt("height", 1).toFloat().coerceAtLeast(1f)
                layerRect.set(-canvasWidth / 2f, -canvasHeight / 2f, canvasWidth / 2f, canvasHeight / 2f)
                live2D = manifest.optString("renderer") == "live2d"
                rig = manifest.optJSONObject("rig") ?: JSONObject()
                motionEnabled = live2D && WallpaperMotion.usesGyro(manifest)
                targetTiltX = 0f; targetTiltY = 0f; tiltX = 0f; tiltY = 0f
                if (!motionEnabled) sensor.stop() else if (lifecycle.visible && authorized()) sensor.start()
                sensitivity = manifest.optDouble("sensitivity", 1.0).toFloat().coerceIn(0f, 2f)
                fallback = decode(File(directory, manifest.optString("originalFileName", "original.img")))
                val items = manifest.getJSONArray("layers")
                decodeSide = minOf(maxDecodedSide, kotlin.math.sqrt(8_000_000.0 / items.length().coerceAtLeast(1)).toInt())
                for (index in 0 until items.length()) {
                    val item = items.getJSONObject(index)
                    val bitmap = decode(File(directory, item.getString("fileName")))
                        ?: error("레이어 이미지를 읽지 못했습니다.")
                    loaded.add(Layer(bitmap, item.optInt("layerOrder", index),
                        item.optDouble("positionX", 0.0).toFloat(), item.optDouble("positionY", 0.0).toFloat(),
                        item.optDouble("rotation", 0.0).toFloat(), item.optDouble("scale", 1.0).toFloat(),
                        item.optDouble("movementX", 0.0).toFloat(), item.optDouble("movementY", 0.0).toFloat(),
                        item.optString("role") == "Background", item.optString("role", "Other"), item.optString("layerId")))
                }
                layers = loaded.sortedBy { it.order }
            } catch (error: OutOfMemoryError) {
                loaded.forEach { it.bitmap.recycle() }
                recycle()
                Log.e("Live2D", "Wallpaper bitmap memory budget exhausted", error)
            } catch (error: Exception) {
                loaded.forEach { it.bitmap.recycle() }
                Log.w("Live2D", "Cannot load wallpaper layers", error)
            }
        }

        private fun decode(file: File): Bitmap? {
            val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
            BitmapFactory.decodeFile(file.absolutePath, bounds)
            if (bounds.outWidth <= 0 || bounds.outHeight <= 0) return null
            val sample = WallpaperBitmapBudget.sampleSize(bounds.outWidth, bounds.outHeight, decodeSide)
            return BitmapFactory.decodeFile(file.absolutePath, BitmapFactory.Options().apply { inSampleSize = sample })
        }

        private fun scheduleDraw() {
            if (lifecycle.visible && lifecycle.surfaceReady && !frameScheduled) { frameScheduled = true; choreographer.postFrameCallback(frame) }
        }

        // The server and web editor use identical rig group names. Separate PNG files are
        // required: a combined body PNG cannot independently bend an arm or hand.
        private fun rigPart(layer: Layer): String {
            if (layer.background) return "fixed"
            layer.cachedRigPart?.let { return it }
            val manual = rig.optJSONObject("partAssignments")?.optString(layer.layerId, "") ?: ""
            val part = if (manual in setOf("fixed", "head", "eye", "frontHair", "backHair", "torso",
                    "leftArm", "rightArm", "leftHand", "rightHand", "lowerBody", "leftLeg", "rightLeg")) manual
            else when (layer.role) {
                "Face", "Eyebrow", "Mouth", "Nose" -> "head"
                "Eye" -> "eye"
                "BackHair" -> "backHair"
                "FrontHair", "Hair" -> "frontHair"
                "Body", "Clothes" -> "torso"
                else -> "fixed"
            }
            layer.cachedRigPart = part
            return part
        }

        private fun rigAngle(layer: Layer): Float {
            if (!live2D || !motionEnabled || !rig.optBoolean("enabled", false)) return 0f
            val part = rigPart(layer)
            if (!rig.optBoolean("faceEnabled", true) && (part == "head" || part == "eye" || part == "frontHair" || part == "backHair")) return 0f
            val head = tiltX.coerceIn(-1f, 1f) * rig.optInt("headTilt", 60) / 100f * .115f
            val follow = rig.optString("bodyMode", "fixed") == "follow"
            val upper = if (follow) head * rig.optInt("upperBody", 40) / 100f else 0f
            val lower = if (follow) head * rig.optInt("lowerBody", 20) / 100f else 0f
            // Previous .60 gain made the default torso rotation less than half a degree.
            val torso = upper * rig.optInt("torso", 50) / 100f * 2.0f
            val leftArm = torso + upper * rig.optInt("leftArm", 35) / 100f * .42f
            val rightArm = torso + upper * rig.optInt("rightArm", 35) / 100f * .42f
            val lowerBody = lower * .55f
            val angle = when (part) {
                "head", "eye" -> head
                "frontHair" -> head * (1f - .55f * rig.optInt("hairSway", 55) / 100f)
                "backHair" -> head * (1f - .28f * rig.optInt("hairSway", 55) / 100f)
                "torso" -> torso
                "leftArm" -> leftArm
                "rightArm" -> rightArm
                "leftHand" -> leftArm + upper * rig.optInt("leftHand", 25) / 100f * .24f
                "rightHand" -> rightArm + upper * rig.optInt("rightHand", 25) / 100f * .24f
                "lowerBody" -> lowerBody
                "leftLeg" -> lowerBody + lower * rig.optInt("leftLeg", 20) / 100f * .18f
                "rightLeg" -> lowerBody + lower * rig.optInt("rightLeg", 20) / 100f * .18f
                else -> 0f
            }
            return Math.toDegrees(angle.toDouble()).toFloat()
        }

        private fun rigShiftX(part: String, angle: Float): Float =
            if (!live2D || !rig.optBoolean("enabled", false)) 0f else RigTranslation.x(part,
                tiltX, angle, canvasWidth, rig.optInt("headTilt", 60), rig.optInt("eyeMove", 35),
                rig.optBoolean("faceEnabled", true), rig.optString("bodyMode", "fixed") == "follow")

        private fun rigShiftY(part: String): Float =
            if (!live2D || !rig.optBoolean("enabled", false)) 0f else RigTranslation.y(part,
                tiltY, canvasHeight, rig.optInt("headTilt", 60), rig.optInt("eyeMove", 35),
                rig.optBoolean("faceEnabled", true), rig.optString("bodyMode", "fixed") == "follow")

        private fun rigPivotX(width: Float): Float =
            (rig.optInt("pivotX", 50).coerceIn(0, 100) / 100f - .5f) * width

        private fun rigPivotY(layer: Layer, height: Float): Float {
            val pivotY = when (rigPart(layer)) {
                "torso", "leftArm", "rightArm", "leftHand", "rightHand" -> rig.optInt("torsoPivotY", 40)
                "lowerBody", "leftLeg", "rightLeg" -> rig.optInt("lowerPivotY", 68)
                else -> rig.optInt("pivotY", 16)
            }
            return (pivotY.coerceIn(0, 100) / 100f - .5f) * height
        }

        private fun draw(canAnimate: Boolean = motionEnabled && authorized()) {
            if (!lifecycle.surfaceReady || !surfaceHolder.surface.isValid) return
            val drawingSurface = surfaceHolder.surface
            val rendered = WallpaperFrameRenderer.render(
                acquire = { if (Build.VERSION.SDK_INT >= 26) surfaceHolder.lockHardwareCanvas()
                    else drawingSurface.lockHardwareCanvas() },
                draw = { canvas ->
                    canvas.drawColor(Color.BLACK)
                    if (layers.isEmpty()) fallback?.let { drawBitmap(canvas, it, 0f, 0f, 0f, 1f) }
                    else {
                        val cover = if (agent) max(canvas.width / canvasWidth, canvas.height / canvasHeight) else 1f
                        for (layer in layers) {
                            if (agent) {
                                val moving = canAnimate && !layer.background
                                val angle = if (moving) rigAngle(layer) else 0f
                                val part = rigPart(layer)
                                val shiftX = if (moving) rigShiftX(part, angle) else 0f
                                val shiftY = if (moving) rigShiftY(part) else 0f
                                val x = if (moving) -tiltX.coerceIn(-1f, 1f) * layer.movementX + shiftX else 0f
                                val y = if (moving) tiltY.coerceIn(-1f, 1f) * layer.movementY + shiftY else 0f
                                canvas.save()
                                canvas.translate(canvas.width / 2f + (layer.positionX + x) * cover, canvas.height / 2f + (layer.positionY + y) * cover)
                                canvas.rotate(layer.rotation)
                                canvas.scale(cover * layer.scale, cover * layer.scale)
                                if (angle != 0f) {
                                    val px = rigPivotX(canvasWidth)
                                    val py = rigPivotY(layer, canvasHeight)
                                    canvas.translate(px, py)
                                    canvas.rotate(angle)
                                    canvas.translate(-px, -py)
                                }
                                canvas.drawBitmap(layer.bitmap, null, layerRect, paint)
                                canvas.restore()
                                continue
                            }
                            // A character-only stack may start with its torso rather than a background.
                            val moving = canAnimate && (!live2D || !layer.background)
                            val amplitudeX = if (live2D) canvas.width * .035f else layer.movementX * 4f
                            val amplitudeY = if (live2D) canvas.height * .035f else layer.movementY * 4f
                            val angle = if (moving) rigAngle(layer) else 0f
                            val part = rigPart(layer)
                            val shiftX = if (moving) rigShiftX(part, angle) else 0f
                            val shiftY = if (moving) rigShiftY(part) else 0f
                            val imageCover = max(canvas.width / canvasWidth, canvas.height / canvasHeight)
                            drawBitmap(canvas, layer.bitmap,
                                layer.positionX + (if (moving) -tiltX * amplitudeX * sensitivity else 0f) + shiftX * imageCover,
                                layer.positionY + (if (moving) tiltY * amplitudeY * sensitivity else 0f) + shiftY * imageCover,
                                layer.rotation, layer.scale * if (live2D) 1.08f else 1f,
                                angle,
                                if (angle != 0f) rigPivotX(layer.bitmap.width.toFloat()) else 0f,
                                if (angle != 0f) rigPivotY(layer, layer.bitmap.height.toFloat()) else 0f)
                        }
                    }
                },
                post = { if (Build.VERSION.SDK_INT >= 26) surfaceHolder.unlockCanvasAndPost(it)
                    else drawingSurface.unlockCanvasAndPost(it) },
                failed = { Log.w("Live2D", "Wallpaper surface changed while drawing", it) })
            val path = loadedManifest
            if (rendered && layers.isNotEmpty() && agent && !isPreview && authorized() && path != null &&
                CompanionLiveEngines.rendered(this, renderingTarget, path)) CompanionRuntime.pollSoon()
        }

        private fun drawBitmap(canvas: Canvas, bitmap: Bitmap, x: Float, y: Float, rotation: Float, layerScale: Float,
                               rigRotation: Float = 0f, pivotX: Float = 0f, pivotY: Float = 0f) {
            val scale = max(canvas.width.toFloat() / bitmap.width, canvas.height.toFloat() / bitmap.height) * layerScale
            canvas.save()
            canvas.translate(canvas.width / 2f + x, canvas.height / 2f + y)
            canvas.rotate(rotation); canvas.scale(scale, scale)
            if (rigRotation != 0f) {
                canvas.translate(pivotX, pivotY)
                canvas.rotate(rigRotation)
                canvas.translate(-pivotX, -pivotY)
            }
            canvas.drawBitmap(bitmap, -bitmap.width / 2f, -bitmap.height / 2f, paint)
            canvas.restore()
        }
    }
}
