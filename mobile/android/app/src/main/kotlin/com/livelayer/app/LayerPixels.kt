package com.livelayer.app

import kotlin.math.roundToInt

/** CPU hole fill for the small background plate; no subject pixels seed the fill. */
internal object LayerPixels {
    fun foreground(pixels: IntArray, mask: FloatArray): IntArray {
        require(pixels.size == mask.size)
        return IntArray(pixels.size) { i ->
            val alpha = ((mask[i] - .05f) / .9f).coerceIn(0f, 1f)
            val sourceAlpha = pixels[i] ushr 24
            ((sourceAlpha * alpha).roundToInt() shl 24) or (pixels[i] and 0x00ffffff)
        }
    }

    fun inpaint(pixels: IntArray, holes: BooleanArray, width: Int, height: Int): IntArray {
        require(width > 0 && height > 0 && pixels.size == width * height && holes.size == pixels.size)
        require(holes.any { !it }) { "배경 영역을 찾지 못했습니다. 다른 사진을 선택해 주세요." }
        val result = pixels.copyOf()
        val known = BooleanArray(pixels.size) { !holes[it] }
        val queue = IntArray(pixels.size)
        var tail = 0
        for (i in pixels.indices) if (known[i]) queue[tail++] = i
        var head = 0
        fun fill(i: Int) {
            if (!known[i]) {
                var r = 0; var g = 0; var b = 0; var count = 0
                fun sample(n: Int) {
                    if (known[n]) {
                        r += (result[n] ushr 16) and 255
                        g += (result[n] ushr 8) and 255
                        b += result[n] and 255
                        count++
                    }
                }
                if (i % width > 0) sample(i - 1)
                if (i % width < width - 1) sample(i + 1)
                if (i >= width) sample(i - width)
                if (i < pixels.size - width) sample(i + width)
                result[i] = (255 shl 24) or ((r / count) shl 16) or ((g / count) shl 8) or (b / count)
                known[i] = true
                queue[tail++] = i
            }
        }
        while (head < tail) {
            val i = queue[head++]
            if (i % width > 0) fill(i - 1)
            if (i % width < width - 1) fill(i + 1)
            if (i >= width) fill(i - width)
            if (i < pixels.size - width) fill(i + width)
        }
        // Diffuse only the reconstructed region, keeping the observed background intact.
        repeat(24) {
            for (i in pixels.indices) if (holes[i]) {
                var r = 0; var g = 0; var b = 0; var count = 0
                fun sample(n: Int) {
                    r += (result[n] ushr 16) and 255
                    g += (result[n] ushr 8) and 255
                    b += result[n] and 255
                    count++
                }
                if (i % width > 0) sample(i - 1)
                if (i % width < width - 1) sample(i + 1)
                if (i >= width) sample(i - width)
                if (i < pixels.size - width) sample(i + width)
                if (count > 0) result[i] = (255 shl 24) or ((r / count) shl 16) or ((g / count) shl 8) or (b / count)
            }
        }
        return result
    }
}
