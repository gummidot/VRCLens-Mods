# VRCLens

Avatar-mounted camera system for VRChat with depth of field, exposure control, drone mode, and simulated sensor sizes. Works in both VR and desktop mode. Made by Hirabiki.

- Gumroad: https://hirabiki.gumroad.com/l/rpnel
- Demo World: https://vrchat.com/home/world/wrld_85bfeefb-78b8-444c-b4e8-15698fb7864d
- Installation docs: https://docs.google.com/document/d/1YqKdZEr36GMuExzHze7eIEDZYBVd_6fJsRASOPaRFUc
- User manual (outdated): https://docs.google.com/document/d/1dqHmiZuTrKXd__rsto2zOR3SzK8KH0vu0y-e2Sv121I

**Installed version:** 1.9.2 (latest available: 1.10.0)
**Min expression parameters:** 24 bits synced
**Min GPU:** GTX 1660
**Material slots:** 6

---

## Installation

1. Set up your avatar project in VRChat Creator Companion (VCC)
2. Import the VRCLens .unitypackage
3. In the scene, locate the `VRCLensSetup` component under `Assets/Hirabiki/VRCLens/Prefabs/VRCLens.prefab`
4. Drag it into your scene (not into the avatar hierarchy directly)
5. In the inspector:
   - Assign your **Avatar Descriptor**
   - Choose **Setup Mode** (VR Left Hand or VR Right Hand)
   - Set **Sensor Resolution** (1080p default, up to 8K)
   - Configure initial defaults: focal length, aperture, sensor size, tonemap, AF mode
   - Optionally enable radial puppets for Zoom, Exposure, Aperture, Focus
   - Toggle Write Defaults ON/OFF to match your avatar's convention
6. Click the setup button to merge VRCLens into your avatar's FX controller and expression menus
7. Upload your avatar

---

## How It Works

VRCLens adds a secondary camera to your avatar that renders to a texture. A custom shader (`DepthOfField.shader`) processes the camera feed through multiple passes to produce depth of field blur, exposure adjustments, tone mapping, filters, and on-screen overlays. The processed image is displayed on a screen mesh attached to your hand (VR) or as a desktop overlay.

Photos are captured by opening VRChat's built-in camera while VRCLens is active. The VRChat camera will display VRCLens's processed output instead of its normal view.

---

## Basic Workflow

1. **Enable** - Action Menu > Expressions > VRCLens > Enable
2. **Configure** - Adjust zoom, aperture, exposure, focus mode, and other settings
3. **Capture** - Open VRChat's built-in camera and press its shutter button. While VRCLens is active, the VRChat camera shows VRCLens's output

> VRCLens cannot save photos on its own. You always need VRChat's built-in camera to capture the final image.

---

## Good Default Settings

| Setting | Value | Why |
|---|---|---|
| AF Mode | Auto Focus or Avatar AF | Auto-focuses where the focus pen points; Avatar AF ignores scenery |
| DoF Mode | Enabled | Enables depth of field blur |
| Aperture | Mid-range (F8-F11) | Sharp photos with deep focus |
| Exposure | ~50% | Neutral brightness (maps to 1x multiplier) |
| Tonemap | ACES or EVILS | Filmic color response with highlight rolloff |
| Stabilize | On | Image stabilization with auto-straighten |
| Grid | On | Rule-of-thirds overlay for composition |
| Sensor | Full Size (1.0x) | Maximum field of view for the focal length |

Increase Aperture (lower F-number) when you want background blur (bokeh). Higher sensor scaling (APS-C, MFT) crops the image and increases effective focal length.

---

## Full Settings Reference

### Root Menu

| Control | Type | Description |
|---|---|---|
| **Enable** | Button | Turn VRCLens on (val=254) |
| **Advanced** | SubMenu | Extended controls for exposure, drone tracking, extras |
| **Zoom** | Radial | Focal length adjustment |
| **Drone** | SubMenu | Free camera movement controls |
| **Aperture** | SubMenu | F-stop, sensor type, bokeh shape, DoF mode |
| **Settings** | SubMenu | Picture style, filters, stabilize, display, portrait |
| **Focus** | SubMenu | Manual/auto focus, avatar AF, focus point movement |
| **Disable** | Button | Turn VRCLens off (val=255) |

### Advanced Menu

| Control | Type | Description |
|---|---|---|
| **Exposure** | SubMenu | EV dial, picture style, auto exposure, white balance |
| **Quick Selfie** | Button | Flip camera to face you |
| **Zoom** | Radial | Focal length (same as root) |
| **Extra...** | SubMenu | Avatar-drop, 3D mode, mount swap, hide lens |
| **Drop** | Button | World-lock the camera at its current position |
| **Movie Mode** | Button | DirectCast: enables a direct-render camera so other players can see your VRCLens feed live |
| **Focus** | SubMenu | Same focus submenu as root |

### Advanced > Exposure

| Control | Type | Description |
|---|---|---|
| **EV Dial** | Radial | Exposure compensation. 50% = neutral (1x). Lower = darker, higher = brighter |
| **EV+** | Button | Step exposure up |
| **Picture Style** | SubMenu | Tone mapping presets (Off/ACES/EVILS/HLG/Neutral) |
| **Reset** | Button | Reset exposure to default |
| **Auto Exposure** | Button | Toggle auto exposure metering |
| **EV-** | Button | Step exposure down |
| **White Bal.** | SubMenu | White balance adjustment (warm/cool/reset) |

### Advanced > Exposure > Picture Style (Tone Mapping)

| Control | Description |
|---|---|
| **Off (RAW)** | No tone mapping, linear output |
| **ACES** | Academy Color Encoding System filmic curve |
| **EVILS** | Filament PBR engine tone mapper |
| **HLG** | Hybrid Log-Gamma HDR curve |
| **Neutral** | Highlight-preserving neutral curve |

### Advanced > Exposure > White Balance

Step controls for adjusting color temperature (warmer/cooler) and reset.

### Advanced > Extra

| Control | Type | Description |
|---|---|---|
| **Avatar-Drop** | Button | Drop camera at avatar position (follows you) |
| **3D Mode** | Button | Side-by-side stereo rendering |
| **Zoom** | Radial | Focal length |
| **Mount Swap** | Button | Switch camera between VR hand and desktop mount |
| **Aperture** | SubMenu | Full aperture controls |
| **Hide Lens** | Button | Hide the camera model from other players |

### Drone Menu

| Control | Type | Description |
|---|---|---|
| **Hand-Rotate** | Button | Rotate camera angle by tilting your hand |
| **Change Angle** | 2-Axis | Puppet for rotating camera yaw/pitch |
| **Tracking** | SubMenu | Pivot tracking, smoothing, vertical lock |
| **Drop** | Button | World-lock the drone |
| **Move Camera** | 2-Axis | Puppet for moving camera position (pan) |
| **Track Self...** | SubMenu | Self-tracking presets (face, up, down, feet) |
| **Drone Speed** | Radial | Movement speed multiplier |

### Drone > Tracking

| Control | Type | Description |
|---|---|---|
| **Move Pivot** | 2-Axis | Reposition the tracking pivot point |
| **Track Pivot** | Button | Lock drone aim to the pivot point |
| **Smoothing** | Radial | Camera movement smoothing amount |
| **Drop** | Button | World-lock drone |
| **Drop Pivot** | Button | World-lock the pivot point |
| **Track Self** | Button | Aim drone at yourself |
| **Vertical Lock** | Button | Lock vertical axis during tracking |

### Drone > Track Self

| Control | Description |
|---|---|
| **Look At Face** | Point camera at your face |
| **Look Up** | Angle up from face |
| **Look Down** | Angle down from face |
| **Look At Feet** | Point camera at your feet |
| **Track Self** | General self-tracking mode |

### Aperture Menu

| Control | Type | Description |
|---|---|---|
| **Increase** | Button | Step F-number up (less blur, deeper focus) |
| **Sensor Type** | SubMenu | Sensor size presets (Full/APS-H/APS-C/MFT/1") |
| **DoF Mode** | Button | Toggle depth of field on/off |
| **Av Dial** | Radial | Continuous aperture adjustment |
| **Decrease** | Button | Step F-number down (more blur, shallower focus) |
| **Bokeh Shape** | SubMenu | Blur disc shape (Circle/Hexagon/Octagon/Square) |

### Aperture > Sensor Type

| Preset | Crop Factor | Effect |
|---|---|---|
| **Full Size** | 1.0x | Standard field of view |
| **APS-H** | 1.3x | Slight crop |
| **APS-C (1.5x)** | 1.5x | Moderate crop (Nikon/Sony equivalent) |
| **APS-C (1.6x)** | 1.6x | Moderate crop (Canon equivalent) |
| **MFT** | 2.0x | Significant crop (Micro Four Thirds) |
| **1"** | 2.7x | Heavy crop (compact camera sensor) |

### Settings Menu

| Control | Type | Description |
|---|---|---|
| **Picture Style** | SubMenu | Tone mapping (same as Advanced > Exposure > Picture Style) |
| **Filters** | SubMenu | Image filter presets |
| **Zoom** | Radial | Focal length |
| **Stabilize** | Button | Image stabilization with horizon leveling |
| **Aperture** | SubMenu | Same aperture controls |
| **Portrait** | Button | Switch to portrait (vertical) orientation |
| **Display** | SubMenu | HUD, grid, overlays, focus peaking |

### Settings > Filters

| Filter | Description |
|---|---|
| **Normal** | No filter |
| **Depth Cool** | Cool-toned depth-based color grading |
| **Depth Cute** | Warm/soft depth-based color grading |
| **Spotlight** | Spotlight/vignette depth effect |
| **Green Back** | Green screen background replacement |
| **Blue Back** | Blue screen background replacement |

### Focus Menu

| Control | Type | Description |
|---|---|---|
| **Manual Focus** | Radial | Adjust focus distance manually |
| **Zoom** | Radial | Focal length |
| **Auto Focus** | Button | Enable auto focus (uses touch pen position) |
| **Display** | SubMenu | HUD/grid/peaking/overlay controls |
| **Avatar AF** | Button | Auto focus that only targets avatars, ignoring world geometry |
| **Move Focus** | 2-Axis | Move the auto focus point on screen |

### Display Menu

| Control | Type | Description |
|---|---|---|
| **V. Horizon** | Button | Toggle virtual horizon indicator |
| **Grid** | Button | Toggle rule-of-thirds grid |
| **Show/Hide** | Button | Toggle overlay visibility |
| **Monitor** | Button | Toggle viewfinder HUD display |
| **Move HUD** | 2-Axis | Reposition the HUD overlay |
| **Focus Peaking** | SubMenu | Focus peaking color selection |

### Display > Focus Peaking

Toggle focus peaking on/off and choose highlight color: Red, Yellow, White, Green, Blue, Magenta.

---

## Tips

- **All settings reset on avatar load** unless marked as Saved in the expression parameters. Zoom, Exposure, Aperture, and Focus radials are saved by default.
- **Sensor Type** affects both field of view and depth of field characteristics. Smaller sensors (MFT, 1") give deeper DOF for the same framing.
- Resolution can be changed via the sensor resolution setting in the VRCLens setup inspector (1080p/1440p/4K/8K). Higher resolution increases GPU cost significantly.
