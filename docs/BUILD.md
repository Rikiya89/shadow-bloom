# Build for Meta Quest

## Requirements

- Unity 6000.6.7f1 with Android Build Support, SDK, NDK and OpenJDK.
- Package dependencies restored from `Packages/manifest.json`.
- A Quest device in developer mode for USB installation and testing.

## Build and install

1. Open `Assets/ShadowBloom/Scenes/ShadowBloom.unity`.
2. Select **Shadow Bloom > Configure Quest Project (Android)** to apply the project's Android configuration: IL2CPP, ARM64, Linear colour and Vulkan. Review any project-setting changes before keeping them.
3. In **File > Build Profiles**, select Android and include the ShadowBloom scene.
4. Build to `Builds/Android/ShadowBloom.apk`.
5. Install with `adb install -r Builds/Android/ShadowBloom.apk`, or use Meta Quest Developer Hub.

The default application identifier is `com.shadowbloom.mrpuzzle`. Use the appropriate application identifier and release signing configuration for your own store upload. Signing credentials and keystores are not included.

On first launch, allow spatial data access for table detection. With no usable room scan, follow the manual placement instructions.

## Editing the scene

The game is stored in prefabs under `Assets/ShadowBloom/Prefabs/`. Edit these assets rather than scene instances. **Shadow Bloom > Build Scene** regenerates the scene from those prefabs; scene-only changes may be lost.
