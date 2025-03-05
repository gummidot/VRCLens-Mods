# VRCLens Mods

VRCLens mods with drag-n-drop VRCFury prefabs

[**Download the latest version**](https://github.com/gummidot/VRCLens-Mods/releases/tag/v1.7.0)

## Requirements

- [VRCFury](https://vrcfury.com/download)

## Installation

Each mod is a VRCFury prefab that you drag-n-drop onto your avatar to install.
You can pick which ones to install, then easily remove them by deleting the prefab.
All mods are local-only or take no extra parameter memory.

1. Import the `VRCLens_Custom` Unity package and find the `Assets/VRCLens_Custom` folder.
1. Drag any of the prefabs onto the `VRCLens` object on your avatar.
   - **Note:** If you re-apply VRCLens, the prefabs will disappear, so you will have to do this again.

![!VRCFury Installation](Doc/VRCFury_Install.png)

> [!WARNING]
> Mods have only been tested with the versions of VRCLens listed below. They could be broken in very old versions, but should work in most recent versions.

## Mods

- [SmoothRotate](#smoothrotate) - Adds a slider that smooths out camera movement
- [DroneSpeed](#dronespeed) - Modifies the Drone Speed slider to go slower or faster than default
- [MoveDroneVertical](#movedronevertical) - Adds a puppet menu to move the drone vertically
- [SmoothZoom](#smoothzoom) - Adds slight smoothing to the Zoom slider
- [ManualFocus (9m)](#manualfocus-9m) - Limits the Manual Focus slider to 9m
- [VRCLensOptimizer](#vrclensoptimizer) - Removes optional components from VRCLens (materials, poly count)
- [CustomResolution](#customresolution) - Overrides the camera resolution and anti-aliasing
- [FarClipPlane](#farclipplane) - Increases the camera's far clipping plane
- [MaxBlurSize](#maxblursize) - Adjusts the maximum blur size for performance
- [FixAvatarDrop](#fixavatardrop) - Fixes the Avatar Drop feature broken in VRCLens 1.9.1 and above
- [AvatarOffset](#avataroffset) - Offsets the camera from your avatar while keeping hand control

## SmoothRotate

**Adds a slider that smooths out camera movement**

Works in both desktop and VR, and can smooth much more than OVR-SmoothTracking.

Last tested: VRCLens 1.9.2

### Usage

The slider will be in your menu under `VRCLens/Custom/SmoothRotate`.
0% is the minimum (default) smoothing, and 100% is the maximum amount of smoothing.

Make sure Stabilize mode is on (the white/yellow hand icon) for this to work.

<video src="https://github.com/user-attachments/assets/05d5c2fd-28e6-4f38-8b98-11be5db84a1b"></video>

### Credits

Thanks to [Minkis](https://www.youtube.com/watch?v=XMcTfFoNUHA) for explaining how to do this.

## DroneSpeed

**Modifies the Drone Speed slider to go slower or faster than default**

There are two versions, only add one to your avatar:

- **Slower** allows the drone to move much slower. 0% is now zero speed.
- **Slower and Faster** allows the drone to move much slower and much faster. 0% is now zero speed, 75% is the original max speed, and 100% is 32x the original max speed.

Last tested: VRCLens 1.9.2

### Usage

Use the built-in Drone Speed slider as usual.

<video src="https://github.com/user-attachments/assets/672eee73-1523-4737-9267-767bda7d8efb"></video>

## MoveDroneVertical

**Adds a puppet menu to move the drone vertically**

The usual way to move the drone vertically is to use gestures to switch between forward/back and up/down movement. With a separate puppet menu, you can move the drone vertically more easily, and also move both forward/back and up/down at the same time.

Last tested: VRCLens 1.9.2

### Usage

The puppet menu will be in your menu under `VRCLens/Custom/Move Drone Vertical`.

<video src="https://github.com/user-attachments/assets/172956c2-84d5-4f11-9ad8-f93599b73564"></video>

## SmoothZoom

**Adds slight smoothing to the Zoom slider**

Last tested: VRCLens 1.9.2

### Usage

Use the built-in Zoom slider as usual.

<video src="https://github.com/user-attachments/assets/b9be523d-e54e-4b8c-bd44-dd43ec843ce1"></video>

## ManualFocus (9m)

**Limits the Manual Focus slider to 9m**

Limits the Manual Focus slider to a maximum of 9m, or 50% in the original slider. Also adds a very small amount of smoothing. Use this for finer control over focus at short distances.

Last tested: VRCLens 1.9.2

### Usage

Use the built-in Manual Focus slider as usual.

<video src="https://github.com/user-attachments/assets/9f8496e8-8a36-44f0-b450-0b3474b765f4"></video>

## VRCLensOptimizer

**Removes optional components from VRCLens (materials, poly count)**

Remove up to 5 optional components on VRCLens for performance optimization:

- **Default camera model** (1 material, 466 tris)
   - Can be removed since it's just cosmetic.
- **Pivot indicator** (1 material, 194 tris)
   - Can be removed if you don't use the pivot feature.
- **Focus pointer for VR only** (1 material, 12 tris)
   - The blue pointer on your off-hand finger that lets you move focus in VR.
- **Avatar auto-focus** (1 material, 12 tris)
   - Can be removed if you don't use avatar AF.
- **Hand preview / HUD for VR only** (1 material, 4 tris)
   - Can be removed if you always use an external desktop overlay. You won't be able to see camera settings like zoom level anymore though.

Last tested: VRCLens 1.9.2

### Usage

Drag and drop the `VRCLensOptimizer` prefab onto the `VRCLens` object on your avatar. Check the components you want to remove. Components are removed on upload, so check your avatar stats in game for the actual material/poly count.

![VRCLensOptimizer](Doc/VRCLens_Optimizer.png)

## CustomResolution

**Overrides the camera resolution and anti-aliasing**

Usually, the sensor resolution and anti-aliasing can only be set when installing VRCLens. This lets you change the resolution and anti-aliasing without having to reinstall VRCLens.

It also adds experimental support for full SBS 3D. VRCLens currently uses half SBS for its side-by-side 3D mode, so recording in 3D at 1920x1080 would produce a 1920x1080 video at half the horizontal resolution (960x1080 per eye). Full SBS would allow you to record at 3840x1080 and produce a 1920x1080 video at full resolution (1920x1080 per eye).

Last tested: VRCLens 1.9.2

### Usage

Enter your custom resolution and/or anti-aliasing in **Override Resolution** and **Override Anti-Aliasing**.

Optionally click **Use Full SBS 3D (experimental)** if you want to enable that. If so, make sure you change the resolution so the width is doubled.

![CustomResolution](Doc/CustomResolution.png)

## FarClipPlane

**Increases the camera's far clipping plane**

In some worlds, far objects disappear in VRCLens because of its short far clip plane of `32000`.
This adds a local-only slider that increases the far clipping plane up to `128000`.

Last tested: VRCLens 1.9.2

### Usage

The slider will be in your menu under `VRCLens/Custom/FarClipPlane`.

<video src="https://github.com/user-attachments/assets/bb43007a-006c-4075-aa23-1c9b4624e407"></video>

Test worlds: [Tulip Riverie․․․](https://vrchat.com/home/world/wrld_fcad2657-05c6-4226-ac5d-9cd1688beb74/info), [Cycle of Life](https://vrchat.com/home/world/wrld_cd085851-4baf-4fb8-9a2a-e0e20f686502/info)

## MaxBlurSize

**Adjusts the maximum blur size for performance**

Adds a local-only slider that lets you adjust the maximum blur size when using DoF, which you can use to improve performance (lower blur size = better performance) or change how the blurring looks.

Last tested: VRCLens 1.9.2

### Usage

The slider will be in your menu under `VRCLens/Custom/MaxBlurSize`.

At 0%, the slider has no effect so it uses whatever blur size you installed VRCLens with. After 0%, the slider increases blur size from `Very Small` up until `Very Large` (see VRCLens installer for the different options).

<video src="https://github.com/user-attachments/assets/d929ee5a-3fec-4bab-8f0e-3e6255932236"></video>

## FixAvatarDrop

**Fixes the Avatar Drop feature broken in VRCLens 1.9.1 and above**

Avatar Drop is bugged in VRCLens 1.9.1 and above (as of VRCLens 1.9.2). This prefab fixes it automatically.

Last tested: VRCLens 1.9.2

### Usage

Use the **Advanced > Extra > Avatar-Drop** toggle as usual. It should now work.

## AvatarOffset

**Offsets the camera from your avatar while keeping hand control**

Offsets the camera from your avatar like Avatar Drop, except you can still move the camera with your hand. As if you had a mirrored clone that was further, taller, or shorter than you actually are.

Last tested: VRCLens 1.9.2

### Usage

There will be 3 toggles in your menu under `VRCLens/Custom/AvatarOffset`:

- **AvatarOffset** enables the avatar offset. Once toggled, use the Drone to move the camera away from you.
- **Rotate With Avatar** locks the camera's rotation with your avatar rotation. By default, the camera stays in place when you rotate.
- **Drop (Reset to Hand)** resets the camera back to your hand. It is the same as the Drone Drop button, just here for convenience.

<video src="https://github.com/user-attachments/assets/8cfbe8ca-1adb-4b94-802d-95cf99186c06"></video>

## Other Mods

Related VRCLens mods that aren't included in the package.

### VRCLens as a VRCFury Prefab

VRCLens directly modifies your avatar’s FX controller, menu, and parameters, making it hard to share or have different versions of your avatar with/without VRCLens.

If you convert VRCLens to a VRCFury prefab, VRCLens can be set up once, then drag-n-drop'd to different avatar versions, and then be easily deleted.

This is a manual process - see guide at https://gummidot.notion.site/VRCLens-as-a-VRCFury-Prefab-15623187a377802fbc17d0357e56f8bc
