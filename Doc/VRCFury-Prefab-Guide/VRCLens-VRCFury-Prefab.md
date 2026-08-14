# VRCLens as a VRCFury Prefab

**Convert VRCLens to a drag-n-drop VRCFury prefab**

VRCLens directly modifies your avatar's FX controller, menu, and parameters, making it hard to share or have different versions of your avatar with/without VRCLens.

As a VRCFury prefab, VRCLens can be set up once, then drag-n-drop'd to different avatar versions, and then be easily deleted :D

![VRCFury prefab result](images/20-vrcfury-prefab.png)

**Last updated:**

- 2026-05-19: Added compatibility note for AvatarPoseSystem
- 2025-02-09: Added steps to get Drone Track Self working (see step 12)
- 2025-01-16 for VRCLens 1.9.2

## Requirements

- **VRCFury**: https://vrcfury.com/
- **VRCLens**

## How to do it

1. In your **Project** files, create a new folder under **Assets** to store files for the VRCFury prefab. Or use an existing folder. I just made a new folder called "VRCLens"

   ![Create folder](images/00-create-folder.png)

2. Right-click inside the folder and **Create** a new **VRChat > Expressions Parameters**, **VRChat > Expressions Menu**, and **Animator Controller**. Give them good names like `FX_VRCLens`, `Menu_VRCLens`, `Params_VRCLens`

   a. Create the Expression Parameters and Menu

   ![Create params and menu](images/01-2-create-params-menu.png)

   b. Create the Animator Controller

   ![Create FX controller](images/01-1-create-fx.png)

   c. Double-click the parameters and delete the `VRCEmote` parameter with the **Minus** button, as it won't be used. The other two parameters should be kept though.

   ![Animator Controller parameters](images/01-create-fx.png)

   ![Delete VRCEmote parameter](images/02-menu-delete-params.png)

3. Temporarily replace your avatar's **FX** layer, **Expressions Menu**, and **Expressions Parameters** with the new files. Remember what the original FX layer, menu, and params were, as you'll have to swap them back in at the end.

   ![Replace FX layer](images/03-replace-fx.png)

   ![Replace menu and params](images/04-replace-menu-params.png)

4. Install VRCLens onto your avatar as usual using the VRCLens installer. Auto-arrange the camera positions, change your settings, then **Apply VRCLens**. VRCLens will then modify the new separate FX/menu/params instead of your original avatar's

   ![Apply VRCLens](images/03-1-apply-vrclens.png)

5. On the `VRCLens` object on your avatar, click **Add Component** and add a `Full Controller (VRCFury)`

   ![Add VRCFury Full Controller](images/05-add-vrcfury-full-controller.png)

6. On the **Full Controller**, click the **Plus** button on the **Controller**, **Menu**, and **Parameters**, and place the new FX controller, menu, and parameters there

   ![Full Controller setup](images/06-vrcfury-full-controller.png)

   a. Click the **Advanced Options** arrow. Under **Path Rewrite Rules**, type `VRCLens` under **If animated path has this prefix**. Leave the other settings alone. In newer VRCFury versions, there is an **Auto-Fix** button that will do the same thing, so you can just click that if you have it.

   ![Path rewrite rules](images/07-vrcfury-path-rewrite.png)

   b. Add **Global Parameters** and set it to `*` to make all VRCLens parameters global. Do this if you use any VRCLens Add-on from this project, or VRCLens with OSC extensions or other mods. Without it, VRCFury renames every VRCLens parameter to keep this Full Controller separate from other prefabs, and nothing outside it can reach them by name.

   ![Global Parameters](images/07-1-global-params.png)

7. Restore the original **FX** layer, **Expressions Menu**, and **Expressions Parameters** on your avatar. If you just wanted to add VRCLens to a single avatar and keep the animator separate, you can stop here and not make the prefab. Otherwise, move on...

   ![Restore FX layer](images/08-restore-fx.png)

   ![Restore menu and params](images/09-restore-menu-params.png)

8. VRCLens puts 3 objects in your avatar armature: `PickupC` on the **Head**, `PickupA` on the **Right Hand**, and `PickupB` on the **Left Hand**. These need to be moved to the prefab.

   **Note**: The left and right hand objects will be swapped if you installed VRCLens on your left hand instead.

   a. Expand your **Armature**, find all 3 objects, and `Ctrl + Click` to select them all

   ![Avatar armature objects](images/10-avatar-objects.png)

   b. Drag them to be directly under the `VRCLens` object instead

   ![Move objects to VRCLens](images/11-avatar-objects-move.png)

9. On `PickupC`, add an `Armature Link (VRCFury)` component.

   ![PickupC Armature Link](images/12-PickupC-armature-link.png)

   a. Set **Link To (Avatar)** to `Head`

   ![PickupC link to Head](images/13-PickupC-armature-link-head.png)

   b. `PickupC` also has a **Scale Constraint** on your avatar neck that's specific to this particular avatar. Right click the `VRCLens` object, and select **Create Empty** to create a new object. Name it `Neck`.

   ![Create Neck object](images/17-create-neck-object.png)

   c. Click the new `Neck` object and add an `Armature Link (VRCFury)` component, with **Link To (Avatar)** set to `Neck`

   ![Neck Armature Link](images/18-neck-armature-link.png)

   d. Go back to `PickupC`, and drag the `Neck` object into the **Scale Constraint** source

   ![PickupC Scale Constraint with Neck](images/19-PickupC-constraint-neck.png)

10. On `PickupA`, add an `Armature Link (VRCFury)` component, with **Link To (Avatar)** set to your main hand, `Right Hand` by default. If you installed VRCLens to your left hand, use `Left Hand` instead.

    ![PickupA Armature Link](images/15-PickupA-armature-link-hand.png)

11. On `PickupB`, add an `Armature Link (VRCFury)` component, with **Link To (Avatar)** set to your other hand, `Left Hand` by default. If you installed VRCLens to your left hand, use `Right Hand` instead.

    ![PickupB Armature Link](images/16-PickupB-armature-link-hand.png)

12. **Optionally**, if you use the **Drone Track Self** feature, the `VRCLens > WorldC > LookAtC` object has constraints on your avatar's **Hips**, **Left Foot**, and **Right Foot** that need to be moved to the prefab for Track Self features to work. You can ignore this if you don't use the Track Self feature.

    ![LookAtC constraints](images/19.1-LookAtC-constraints.png)

    a. Right click the `VRCLens` object, and select **Create Empty** to create a new object. Name it `Hips`. Repeat this two more times to create new objects for `Left Foot` and `Right Foot`. On each object, add an `Armature Link (VRCFury)` component, with **Link To (Avatar)** set to the corresponding body part, `Hips`, `Left Foot`, or `Right Foot`.

    ![Track Self Armature Links](images/19.2-LookAtC-armature-link.png)

    b. In the `LookAtC` constraint sources, replace the 2nd, 3rd, and 4th slots with the `Hips`, `Left Foot`, and `Right Foot` objects you just created.

    ![LookAtC constraint sources](images/19.3-LookAtC-constraint-sources.png)

13. Drag the `VRCLens` object into your **Project** files to create a prefab out of it. It should now be blue in the **Hierarchy**. And it's done, now you can drag/copy the prefab onto any other avatar version to add VRCLens

    ![Drag to create prefab](images/21-vrclens-drag-prefab.png)

    ![Completed prefab](images/20-vrcfury-prefab.png)

    a. To install the prefab onto other avatars, drag and drop the VRCLens prefab onto the avatar, and it's ready to upload

    ![Install prefab on avatar](images/22-vrcfury-prefab-install.png)

14. Repeat the steps if you want to change VRCLens settings, create a prefab for an entirely different avatar, or upgrade VRCLens. You can also make different VRCLens prefabs with different settings if you want.

## Compatibility: AvatarPoseSystem (Unfix Objects)

If you use AvatarPoseSystem (APS) with VRCLens, APS's `Unfix Objects` does not work with VRCFury `Armature Link` and the camera will stay on your avatar when you freeze a pose.

**To fix this:** Replace all VRCFury `Armature Link` components with Modular Avatar's `MA Bone Proxy` (`As child; keep position and rotation`) instead. Then add the VRCLens prefab to APS's `Unfix Objects` list. Everything else should stay the same.
