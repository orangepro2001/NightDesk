plugins { id("com.android.application"); id("org.jetbrains.kotlin.android") }
android {
    namespace = "dev.nightdesk.android"
    compileSdk = 35
    testBuildType = "release"
    defaultConfig {
        applicationId = "dev.nightdesk.android"
        minSdk = 29
        targetSdk = 35
        versionCode = providers.gradleProperty("nightdeskVersionCode").orElse("9").get().toInt()
        versionName = providers.gradleProperty("nightdeskVersionName").orElse("1.3.0").get()
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }
    signingConfigs {
        create("persistentRelease") {
            System.getenv("NIGHTDESK_KEYSTORE")?.let {
                storeFile = file(it)
                storePassword = System.getenv("NIGHTDESK_STORE_PASSWORD")
                keyAlias = "nightdesk"
                keyPassword = System.getenv("NIGHTDESK_STORE_PASSWORD")
            }
        }
    }
    buildTypes { getByName("release") { signingConfig = signingConfigs.getByName("persistentRelease"); isMinifyEnabled = false } }
    compileOptions { sourceCompatibility = JavaVersion.VERSION_17; targetCompatibility = JavaVersion.VERSION_17 }
    kotlinOptions { jvmTarget = "17" }
    buildFeatures { buildConfig = true }
    lint { abortOnError = true }
}
dependencies {
    implementation(files("libs/mobile.aar"))
    implementation("androidx.activity:activity-ktx:1.10.0")
    implementation("androidx.core:core-ktx:1.15.0")
    implementation("com.journeyapps:zxing-android-embedded:4.3.0")
    implementation("com.google.zxing:core:3.5.3")
    implementation("androidx.work:work-runtime-ktx:2.10.0")
    testImplementation("junit:junit:4.13.2")
    testImplementation("org.json:json:20240303")
    androidTestImplementation("androidx.test:core:1.6.1")
    androidTestImplementation("androidx.test:runner:1.6.2")
    androidTestImplementation("androidx.test.ext:junit-ktx:1.2.1")
}
