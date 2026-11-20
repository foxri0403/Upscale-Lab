package com.livelayer.app

/** Shared-canvas offsets matching the web rig's settled pose (no idle animation).
 * The eye adds independent gaze to the head offset, rather than just rotating.
 */
internal object RigTranslation {
    private fun face(part: String) = part == "head" || part == "eye" || part == "frontHair" || part == "backHair"
    private fun body(part: String) = when (part) {
        "torso", "leftArm", "rightArm", "leftHand", "rightHand", "lowerBody", "leftLeg", "rightLeg" -> true
        else -> false
    }
    fun x(part: String, tilt: Float, angleDegrees: Float, width: Float, headTilt: Int, eyeMove: Int,
          faceEnabled: Boolean, followBody: Boolean): Float {
        val input = tilt.coerceIn(-1f, 1f)
        if (face(part)) return if (faceEnabled) width * input *
            (headTilt.coerceIn(0, 100) / 100f * .008f + if (part == "eye") eyeMove.coerceIn(0, 100) / 100f * .018f else 0f) else 0f
        return if (body(part) && followBody) width *
            (Math.toRadians(angleDegrees.toDouble()).toFloat() * .13f + input * .0035f) else 0f
    }
    fun y(part: String, tilt: Float, height: Float, headTilt: Int, eyeMove: Int,
          faceEnabled: Boolean, followBody: Boolean): Float {
        val input = tilt.coerceIn(-1f, 1f)
        if (face(part)) return if (faceEnabled) height * input *
            (headTilt.coerceIn(0, 100) / 100f * .007f + if (part == "eye") eyeMove.coerceIn(0, 100) / 100f * .014f else 0f) else 0f
        return if (body(part) && followBody) height * input * .0025f else 0f
    }
}
