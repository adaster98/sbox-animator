# S&box Weapon Animator

A native, document-based S&box editor workspace for calibrating rigged weapons, binding Facepunch first-person arms, authoring animation clips, and generating a ready-to-use viewmodel package.

## Open the workspace

1. Add this library to an S&box project.
2. In the Asset Browser choose **New → Weapon Animation Project**.
3. Double-click the resulting `.wepanim` asset.

The workspace can also be opened from **Tools → Weapon Animator**. It creates a private editor scene and never inserts preview objects into the active game scene.

## Workflow

- **Calibrate:** import a rigged FBX, SMD, DMX, or VMDL, select the weapon-bone subtree, exclude foreign branches, establish physical scale, set the grip, and optionally place rear/front markers for Auto-align.
- **Animate:** browse the complete grouped rig at full height, pose the selected control with local/world numeric transforms or viewport gizmos, and commit poses to the timeline. To bind a hand, select that hand once in **Controls**, choose its **Attachment bone** in the inspector, then press **Bind**; multi-selection is not required. `weapon_root` is the normal grip attachment. The right column keeps the selected-control inspector above a vertically spacious clip rack, while the dope sheet remains beneath the viewport.
- **Animate visibility:** select an isolated weapon bone such as a main or spare magazine, enable its **Visibility** channel, choose its default state, then key **Visible at playhead** or **Hidden at playhead**. These stepped rows appear above transform tracks in the dope sheet. Bone branches work for rigidly weighted parts; models with bodygroups can use explicit visible/hidden bodygroup values instead.
- **Generate:** write the animation host, one SMD per clip, ModelDoc files, optional AnimGraph, prefab, and ownership manifest beneath `Assets/weapons/<project>/viewmodel/`.

Only manifest-owned outputs are replaced during regeneration.
The viewport toolbar uses the standard **W** Move, **E** Rotate, and **R** Scale bindings.
Its globe button switches the shared Local/World coordinate space; the selected-control
transform labels follow that setting automatically.
The adjacent angle-snap control enables or disables rotation snapping and edits its step
from 0.25° through 180°; its arrow buttons move through the standard editor angle presets.
The top-right camera controls switch between Orbit and Free Look and toggle Lit/Full Bright
rendering. In Free Look, hold the right mouse button to look, use **WASD** to move, hold
**Shift** to move faster, and scroll to adjust movement speed. A brief top-center readout
confirms the current speed.
With Auto-key disabled, transform edits are saved as per-clip working poses until **Key pose**
or **Add key** commits them. Working poses are editor-only and never affect generated SMDs,
hashes, onion skins, or prefab playback.
The dope sheet keeps a full-clip range navigator above its frame ruler. Drag its handles to
set the visible range, drag the highlighted region to pan, or use **Ctrl+scroll** to zoom
around the current midpoint. The ruler and playhead scrub in whole frames. The key grid is
reserved for click and marquee selection, and selected transform or visibility keys can be
dragged together as one undoable frame-snapped edit. A guttered vertical scrollbar exposes
every track while keeping the frame ruler and track-name column fixed.

Viewport guides are disabled for new projects. Grid opacity and line weight can be previewed
live from **Edit → Preferences**; both settings affect the minor, major, and colored origin
axes without affecting generated assets.
The same Preferences window controls the cyan viewport edge light. It can be disabled or
adjusted from 0–12 brightness, with a restrained default of 4. Full Bright disables this
light automatically and gives the Facepunch arms a temporary neutral preview material so
their skin remains visible; neither setting changes generated materials.

Schema-v2 projects are backed up before their first schema-v3 save. Weapon calibration,
anchors, clips, tags, and compatible weapon tracks are preserved, while imported arm
tracks and bindings are reset for the separated-rig workflow.
Early schema-v3 projects with model-space Idle seed keys are repaired on open and receive
a versioned backup before the repaired document is saved.
Generated Idle clips remain locked to the current calibrated bind pose until a deliberate
key edit marks them as authored. Older pristine Idle clips polluted by inspector-selection
writes are restored on open; unbound arm chains remain in the native Facepunch pose.
Viewport gizmo and numeric scrubs preview continuously without rebuilding the surrounding
workspace, then produce one undo action on release. The visible Facepunch arms mesh follows
the evaluated arm pose directly, including finger and IK edits.
Visibility state is exported as deterministic AnimGraph tags and a generated runtime
controller. Graph-free prefabs sample the active sequence directly, so magazine visibility
has the same result in the editor preview and final playback.
