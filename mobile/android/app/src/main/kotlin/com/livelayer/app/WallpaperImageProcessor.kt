package com.livelayer.app

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.ImageDecoder
import android.graphics.Matrix
import android.graphics.Paint
import android.graphics.Point
import android.graphics.RectF
import android.os.Build
import androidx.exifinterface.media.ExifInterface
import java.io.File
import kotlin.math.max
import kotlin.math.min
import kotlin.math.roundToInt

object WallpaperImageProcessor {
    private const val MAX_SIDE = 4096

    fun writePng(source: File, ratio: WallpaperRatio, display: Point, output: File) {
        val bitmap = render(source, ratio, display)
        try {
            output.outputStream().use { stream ->
                check(bitmap.compress(Bitmap.CompressFormat.PNG, 100, stream)) {
                    "배경화면 이미지를 준비하지 못했습니다."
                }
            }
        } finally {
            bitmap.recycle()
        }
    }

    fun render(file: File, ratio: WallpaperRatio, display: Point): Bitmap {
        val source = decode(file)
        try {
            val aspect = ratio.aspectRatio(source.width, source.height, display.x, display.y)
            val crop = WallpaperRatio.centerCrop(source.width, source.height, aspect)
            // Automatic output has exactly the display proportions. Manual output
            // retains the selected crop; Android handles final launcher placement.
            val factor = min(1.0, MAX_SIDE.toDouble() / max(display.x, display.y))
            val width = if (ratio.automatic) max(1, (display.x * factor).roundToInt())
                else max(1, crop.width.roundToInt())
            val height = if (ratio.automatic) max(1, (display.y * factor).roundToInt())
                else max(1, crop.height.roundToInt())
            val output = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
            try {
                val matrix = Matrix().apply { setRectToRect(
                    RectF(crop.left.toFloat(), crop.top.toFloat(),
                        (crop.left + crop.width).toFloat(), (crop.top + crop.height).toFloat()),
                    RectF(0f, 0f, width.toFloat(), height.toFloat()),
                    Matrix.ScaleToFit.FILL) }
                Canvas(output).drawBitmap(source, matrix,
                    Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG))
                return output
            } catch (error: Exception) {
                output.recycle()
                throw error
            }
        } finally {
            source.recycle()
        }
    }

    private fun decode(file: File): Bitmap {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            return ImageDecoder.decodeBitmap(ImageDecoder.createSource(file)) { decoder, info, _ ->
                decoder.allocator = ImageDecoder.ALLOCATOR_SOFTWARE
                val factor = min(1.0, MAX_SIDE.toDouble() / max(info.size.width, info.size.height))
                decoder.setTargetSize(max(1, (info.size.width * factor).roundToInt()),
                    max(1, (info.size.height * factor).roundToInt()))
            }
        }
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeFile(file.absolutePath, bounds)
        require(bounds.outWidth > 0 && bounds.outHeight > 0) { "이미지를 읽을 수 없습니다." }
        var sample = 1
        while (max(bounds.outWidth, bounds.outHeight) / sample > MAX_SIDE) sample *= 2
        val source = BitmapFactory.decodeFile(file.absolutePath,
            BitmapFactory.Options().apply { inSampleSize = sample })
            ?: error("이미지를 읽을 수 없습니다.")
        val orientation = runCatching {
            ExifInterface(file).getAttributeInt(ExifInterface.TAG_ORIENTATION, ExifInterface.ORIENTATION_NORMAL)
        }.getOrDefault(ExifInterface.ORIENTATION_NORMAL)
        val matrix = Matrix()
        when (orientation) {
            ExifInterface.ORIENTATION_FLIP_HORIZONTAL -> matrix.setScale(-1f, 1f)
            ExifInterface.ORIENTATION_ROTATE_180 -> matrix.setRotate(180f)
            ExifInterface.ORIENTATION_FLIP_VERTICAL -> matrix.setScale(1f, -1f)
            ExifInterface.ORIENTATION_TRANSPOSE -> { matrix.setRotate(90f); matrix.postScale(-1f, 1f) }
            ExifInterface.ORIENTATION_ROTATE_90 -> matrix.setRotate(90f)
            ExifInterface.ORIENTATION_TRANSVERSE -> { matrix.setRotate(-90f); matrix.postScale(-1f, 1f) }
            ExifInterface.ORIENTATION_ROTATE_270 -> matrix.setRotate(-90f)
        }
        if (matrix.isIdentity) return source
        try {
            return Bitmap.createBitmap(source, 0, 0, source.width, source.height, matrix, true)
        } finally {
            source.recycle()
        }
    }
}
