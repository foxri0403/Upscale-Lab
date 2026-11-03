import java.io.File
import java.util.Properties
import java.util.Base64
import java.net.URI

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

// Flutter passes dart-defines to Gradle as base64 values. The native service
// uses the same build-time origin, including after reboot; the UI cannot edit it.
val companionDefines = providers.gradleProperty("dart-defines").orNull
    ?.split(",")?.map { String(Base64.getDecoder().decode(it), Charsets.UTF_8) }
    ?.associate { it.substringBefore('=') to it.substringAfter('=', "") } ?: emptyMap()
val companionApiBaseUrl = companionDefines["API_BASE_URL"]
    ?: providers.environmentVariable("UPSCALE_COMPANION_API_BASE_URL").orNull
fun serverLiteral(value: String): String {
    val uri = URI(value)
    require(uri.isAbsolute && uri.scheme in listOf("http", "https") && uri.host != null &&
        uri.userInfo == null && uri.path in listOf("", "/") && uri.query == null && uri.fragment == null) {
        "API_BASE_URL must be an HTTP(S) origin without credentials or a path."
    }
    return "\"${value.trimEnd('/')}\""
}
val configuredServer = companionApiBaseUrl?.let(::serverLiteral)
val companionLocalTest = (companionDefines["COMPANION_LOCAL_TEST"]
    ?: providers.gradleProperty("companionLocalTest").orElse("false").get()).toBooleanStrict()
val companionAllowHttp = (companionDefines["COMPANION_ALLOW_HTTP"]
    ?: providers.gradleProperty("companionAllowHttp").orElse("false").get()).toBooleanStrict()
val validateCompanionRelease = tasks.register("validateCompanionRelease") {
    doLast { check(configuredServer != null) { "Supply --dart-define=API_BASE_URL=... for release builds." } }
}
tasks.matching { it.name == "preReleaseBuild" }.configureEach { dependsOn(validateCompanionRelease) }

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
    buildFeatures { buildConfig = true }

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
        getByName("debug") {
            buildConfigField("String", "COMPANION_API_BASE_URL", configuredServer ?: "\"http://10.0.2.2:5080\"")
            manifestPlaceholders["companionCleartext"] = "true"
        }
        getByName("release") {
            // Never silently substitute this machine's debug certificate.
            signingConfig = signingConfigs.getByName("shared")
            buildConfigField("String", "COMPANION_API_BASE_URL", configuredServer ?: "\"\"")
        }
    }

    defaultConfig {
        manifestPlaceholders["companionCleartext"] = companionAllowHttp.toString()
        applicationId = if (companionLocalTest) "com.livelayer.app.localtest" else "com.livelayer.app"
        manifestPlaceholders["companionAppLabel"] = if (companionLocalTest) "Upscale Lab Connect (Local Test)" else "Upscale Lab Connect"
        minSdk = maxOf(24, flutter.minSdkVersion)
        targetSdk = flutter.targetSdkVersion
        versionCode = flutter.versionCode
        versionName = flutter.versionName
    }
}

flutter { source = "../.." }

dependencies {
    testImplementation("junit:junit:4.13.2")
    testImplementation("org.json:json:20240303")
}
