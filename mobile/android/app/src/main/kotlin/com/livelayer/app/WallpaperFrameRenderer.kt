package com.livelayer.app

/** A surface can disappear between acquisition and posting on older launchers. */
internal object WallpaperFrameRenderer {
    fun <T : Any> render(acquire: () -> T?, draw: (T) -> Unit, post: (T) -> Unit,
        failed: (RuntimeException) -> Unit): Boolean {
        val frame = try { acquire() } catch (error: RuntimeException) { failed(error); null } ?: return false
        var success = true
        try { draw(frame) }
        catch (error: RuntimeException) { success = false; failed(error) }
        finally {
            try { post(frame) }
            catch (error: RuntimeException) { success = false; failed(error) }
        }
        return success
    }
}
