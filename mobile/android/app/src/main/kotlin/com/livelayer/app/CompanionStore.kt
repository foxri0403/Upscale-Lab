package com.livelayer.app

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import org.json.JSONObject
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

internal class CompanionStore(private val context: Context) {
    private val preferences = context.getSharedPreferences("companion_credentials", Context.MODE_PRIVATE)
    private var memory: JSONObject? = null
    private fun key(): SecretKey {
        val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (store.getKey("upscale-companion-v1", null) as? SecretKey)?.let { return it }
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore").apply {
            init(KeyGenParameterSpec.Builder("upscale-companion-v1", KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build())
        }.generateKey()
    }
    @Synchronized fun read(): JSONObject {
        memory?.let { return JSONObject(it.toString()) }
        val loaded = runCatching {
            val encrypted = preferences.getString("state", null) ?: return@runCatching JSONObject()
            val bytes = Base64.decode(encrypted, Base64.NO_WRAP)
            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, bytes.copyOfRange(0, 12)))
            JSONObject(String(cipher.doFinal(bytes.copyOfRange(12, bytes.size)), Charsets.UTF_8))
        }.getOrElse { JSONObject() }
        memory = loaded
        return JSONObject(loaded.toString())
    }
    @Synchronized fun save(state: JSONObject) {
        memory = JSONObject(state.toString())
        val durable = JSONObject(state.toString())
        if (!state.optBoolean("remember")) durable.remove("session")
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, key())
        val bytes = cipher.iv + cipher.doFinal(durable.toString().toByteArray(Charsets.UTF_8))
        check(preferences.edit().putString("state", Base64.encodeToString(bytes, Base64.NO_WRAP)).commit()) { "보안 저장에 실패했습니다." }
    }
}
