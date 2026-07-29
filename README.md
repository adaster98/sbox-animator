# S&box Weapon Animator

A native S&box editor tool for preparing rigged weapon models, posing Facepunch first-person arms, creating weapon animations, and generating a ready-to-use viewmodel.

> [!IMPORTANT]
> This project is currently in **beta**. Core authoring is usable, but bugs and workflow changes should be expected. Keep your project under version control and report anything that behaves unexpectedly.

<img width="2294" height="1298" alt="image" src="https://github.com/user-attachments/assets/8a397125-4e1f-4c2e-bdd5-2de109ecc029" />



## Features

- A dedicated weapon-animation workspace with its own preview scene.
- Import support for rigged FBX, SMD, DMX, and VMDL models.
- Automatic nearby texture discovery with separate materials for each weapon material slot.
- Weapon hierarchy filtering for models containing unwanted arm or camera rigs.
- Visual scale calibration, measurements, alignment tools, and output anchors.
- Facepunch first-person arms with hand targets, elbow controls, finger posing, and reusable grip poses.
- Standard weapon clip slots including Idle, Deploy, Fire, Reload, Holster, Inspect, and incremental reload animations.
- Viewport Move, Rotate, and Scale gizmos with Local/World space and rotation snapping.
- Whole-frame keyframing, marquee selection, dope-sheet navigation, playback controls, and reversible animations.
- Speed and transform-channel curve editing with Bézier handles and easing presets.
- Keyed visibility for magazines and other removable weapon parts.
- Optional generation of a Facepunch-compatible AnimGraph and ready-to-use viewmodel prefab.

The workspace is independent of the normal scene editor. Opening or closing it does not add preview objects to your active game scene.

## Installation

Clone or download this repository into your S&box project's library folder:

```text
<your-project>/Libraries/asterw.sbox-animator/
```

Open or reload the project and allow S&box to compile the library.

## Create a weapon animation project

1. Open the S&box Asset Browser.
2. Choose **New → Weapon Animation Project**.
3. Name the new `.wepanim` asset.
4. Double-click it to open the Weapon Animator.

The workspace can also be opened from **Tools → Weapon Animator**. No active scene or selected GameObject is required.

## 1. Import and calibrate

1. Press **Import rigged model** and select your weapon.
   - For FBX imports, keep common color, normal, roughness, metalness, and ambient-occlusion images in a nearby `Textures` or `Materials` folder. They are matched and previewed automatically.
2. Choose the weapon's hierarchy root.
3. Review the retained bones and exclude unrelated branches such as imported character arms.
4. Set the weapon's physical scale using the reference arms, model dimensions, or measurement tool.
5. Place the **Primary grip** anchor where the firing hand should hold the weapon.
6. Optionally place the front and rear alignment markers if the model needs automatic orientation.
7. Place output anchors such as **Muzzle** and **Eject** where required.
8. Confirm the rig and continue to the animation workspace.

The original imported model is not modified.

If textures are added or changed later, use **Tools → Refresh Materials**. The generated viewmodel receives its own compiled textures and VMAT assets, with specular enabled and metalness enabled when a metalness map is present.

## 2. Bind the arms

The Primary grip anchor is a weapon reference point; it does not automatically pose the hand.

To bind a hand:

1. Select **Primary hand** or **Support hand** from the Rig Browser.
2. Choose its weapon attachment bone in the inspector. `weapon_root` is usually suitable for the primary hand.
3. Press **Bind**.
4. Move and rotate the hand target into position.
5. Adjust the elbow controls and pose the fingers around the weapon.
6. Save the completed setup as the default grip pose.

Each hand is bound independently, so one-handed weapons are supported.

## 3. Animate a clip

1. Select a clip from the Clip Rack.
2. Press **Start**, duplicate another clip, or import an existing animation.
3. Select a weapon bone, arm bone, finger, or control from the Rig Browser.
4. Pose it with the viewport gizmo or the numeric transform fields.
5. Move the playhead and create additional keys.

With **Auto-key** enabled, transform changes immediately create or update a key at the current frame. With Auto-key disabled, changes remain an unkeyed working pose until you press **Key pose** or **Add key**.

Useful timeline tools include:

- Drag the ruler to scrub through whole frames.
- Drag across the key grid to marquee-select keys.
- Drag selected keys to retime them.
- Press **Delete** or **Backspace** to remove selected keys.
- Use the range bar above the ruler to pan or zoom through longer clips.
- Press **Curves** to edit motion speed or individual transform channels.
- Press **Reverse** to reverse selected keys within their range. With nothing selected, it reverses the entire clip—useful when turning a Deploy animation into a Holster animation.
- Toggle the loop icon beside the playback controls to repeat the active clip while refining motion
  and curves.

Custom clips can be renamed or deleted from their Clip Properties. Renaming also gives the generated
sequence a stable, readable name.

## Part visibility

Visibility tracks are useful for reload animations involving a main and spare magazine.

1. Select the weapon bone that controls the part.
2. Enable its visibility channel in the inspector.
3. Set whether the part is visible by default.
4. Add visible or hidden keys at the appropriate frames.

Visibility appears as stepped tracks in the dope sheet and is included in generated playback.

## Viewport controls

| Control | Action |
| --- | --- |
| **W** | Move |
| **E** | Rotate |
| **R** | Scale |
| Globe button | Toggle Local/World space |
| Angle control | Toggle or adjust rotation snapping |
| **Ctrl + scroll** over timeline | Zoom the visible frame range |
| **F** in curve editor | Fit the visible curves |
| Orbit / Free Look buttons | Change camera navigation mode |
| **WASD** in Free Look | Move the camera |
| **Shift** in Free Look | Move faster |
| Scroll in Free Look | Adjust camera speed |

The viewport also supports Lit and Full Bright preview modes, optional guides, grid adjustments, skeleton overlays, and onion skins.

## Generate the viewmodel

Save the `.wepanim` project, then press **Generate**.

The default output is:

```text
Assets/weapons/<weapon-name>/viewmodel/
```

Generation can produce:

- Compiled animation sequences.
- An animation host model.
- A ready-to-use viewmodel prefab.
- An optional Facepunch-compatible AnimGraph.

Only files owned by the Weapon Animator are replaced when generating again.
The Generate button becomes **Cancel** while work is in progress and safely restores the previous
generated files if generation is stopped.

## Beta limitations

- Facepunch first-person human arms are the only supported arm profile.
- Imported weapons must already contain usable bones and skin weights. Automatic material matching depends on recognizable material and texture names.
- Weapon rigs vary considerably, so hierarchy filtering and hand placement require manual review.
- Imported foreign character meshes are not automatically removed from the source model.
- Missing action clips may use idle or no-op fallbacks; Idle is required for generation.
- SMD models can be imported for rig review and authoring, but S&box ModelDoc cannot embed their
  render geometry in a generated viewmodel. Re-export the source as FBX or DMX before generation.

## Reporting problems

Please open a GitHub issue with:

- A short description of what you expected and what happened.
- Steps that reproduce the problem.
- The relevant S&box developer-log error.
- The source model format and a brief description of its rig.
- A screenshot or short recording when the problem is visual.

Avoid uploading models that you do not have permission to redistribute.
