import java.io.File
import java.util.Properties

plugins {
    id("com.android.application")
    id("kotlin-android")
    id("dev.flutter.flutter-gradle-plugin")
}

// Credentials stay outside Git. Environment variables override local properties.
val sharedSigningFile = rootProject.file(
    providers.environmentVariable("UPSCALE_SIGNING_PROPERTIES_FILE")
        .orElse("key.properties").get()
)
val sharedSigningProperties = Properties().apply {
    if (sharedSigningFile.isFile) sharedSigningFile.reader(Charsets.UTF_8).use { load(it) }
}
fun signingValue(property: String, environment: String): String? =
    providers.environmentVariable(environment).orNull
        ?: sharedSigningProperties.getProperty(property)

val sharedStorePath = signingValue("storeFile", "UPSCALE_KEYSTORE_FILE")
val sharedStorePassword = signingValue("storePassword", "UPSCALE_KEYSTORE_PASSWORD")
val sharedKeyAlias = signingValue("keyAlias", "UPSCALE_KEY_ALIAS")
val sharedKeyPassword = signingValue("keyPassword", "UPSCALE_KEY_PASSWORD")
val sharedStoreFile = sharedStorePath?.takeIf { it.isNotBlank() }?.let { path ->
    File(path).let { if (it.isAbsolute) it else File(sharedSigningFile.parentFile, path) }
}

val validateSharedSigning = tasks.register("validateSharedSigning") {
    doLast {
        val missing = listOf(
            "storeFile" to sharedStorePath,
            "storePassword" to sharedStorePassword,
            "keyAlias" to sharedKeyAlias,
            "keyPassword" to sharedKeyPassword,
        ).filter { it.second.isNullOrBlank() }.map { it.first }
        check(missing.isEmpty()) {
            "Shared release signing is not configured. Set ${missing.joinToString()} " +
                "in local key.properties or UPSCALE signing environment variables."
        }
        check(sharedStoreFile?.isFile == true) { "Shared signing keystore was not found." }
    }
}

tasks.matching { it.name == "validateSigningRelease" }.configureEach {
    dependsOn(validateSharedSigning)
}

android {
    namespace = "com.livelayer.app"
    compileSdk = flutter.compileSdkVersion
    ndkVersion = flutter.ndkVersion

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = JavaVersion.VERSION_17.toString() }

    signingConfigs {
        create("shared") {
            storeFile = sharedStoreFile
            storePassword = sharedStorePassword
            keyAlias = sharedKeyAlias
            keyPassword = sharedKeyPassword
        }
    }

    buildTypes {
        getByName("release") {
            // Never silently substitute this machine's debug certificate.
            signingConfig = signingConfigs.getByName("shared")
        }
    }

    defaultConfig {
        manifestPlaceholders["companionCleartext"] = providers.gradleProperty("companionAllowHttp").orElse("false").get()
        applicationId = "com.livelayer.app"
        minSdk = maxOf(24, flutter.minSdkVersion)
        targetSdk = flutter.targetSdkVersion
        versionCode = flutter.versionCode
        versionName = flutter.versionName
    }
}

flutter { source = "../.." }

dependencies {
    implementation("com.google.android.gms:play-services-mlkit-subject-segmentation:16.0.0-beta1")
    implementation("androidx.exifinterface:exifinterface:1.4.2")
    testImplementation("junit:junit:4.13.2")
    testImplementation("org.json:json:20240303")
}
