plugins {
    id("com.android.application")
    id("kotlin-android")
    id("dev.flutter.flutter-gradle-plugin")
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

    // Team test releases must remain upgrade-compatible with the existing APKs.
    signingConfigs {
    create("teamRelease") {
        val keystorePath = System.getenv("UPSCALELAB_KEYSTORE_PATH")
            ?: error("UPSCALELAB_KEYSTORE_PATH 환경변수가 없습니다.")

        val keystorePassword = System.getenv("UPSCALELAB_KEYSTORE_PASSWORD")
            ?: error("UPSCALELAB_KEYSTORE_PASSWORD 환경변수가 없습니다.")

        storeFile = file(keystorePath)
        storePassword = keystorePassword
        keyAlias = "upscalelab"
        keyPassword = keystorePassword
    }
}

buildTypes {
    getByName("release") {
        signingConfig = signingConfigs.getByName("teamRelease")
    }
}

    defaultConfig {
        applicationId = "com.livelayer.app"
        minSdk = flutter.minSdkVersion
        targetSdk = flutter.targetSdkVersion
        versionCode = flutter.versionCode
        versionName = flutter.versionName
    }
}

flutter { source = "../.." }

dependencies {
    implementation("androidx.exifinterface:exifinterface:1.4.2")
    testImplementation("junit:junit:4.13.2")
    testImplementation("org.json:json:20240303")
}
