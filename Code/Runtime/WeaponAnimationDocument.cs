#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Sandbox;

namespace SboxWeaponAnimator;

public enum WeaponAnimatorStage
{
	Calibrate = 1,
	Animate = 2
}

public enum WeaponBoneClassification
{
	WeaponRoot,
	Animatable,
	Structural,
	Ignored
}

public enum WeaponBoneInclusion
{
	Included,
	StructuralBridge,
	Excluded
}

public enum MeasurementUnit
{
	Inches,
	Centimetres
}

public enum WeaponUpAxis
{
	PositiveZ,
	NegativeZ,
	PositiveY,
	NegativeY
}

public enum ClipReadiness
{
	NotStarted,
	Draft,
	Ready,
	Warning
}

public enum WeaponClipRole
{
	Custom,
	Idle,
	Deploy,
	Fire,
	FireDry,
	Reload,
	ReloadEmpty,
	Holster,
	Inspect,
	Sprint,
	Jump,
	Lower,
	Ironsights,
	GrabStance,
	GrabGestureOne,
	GrabGestureTwo,
	GrabGestureThree,
	GrabGestureFour,
	ReloadEnter,
	FirstShell,
	InsertShell,
	ReloadExit
}

public enum TrackInterpolation
{
	Stepped,
	Linear,
	Cubic
}

public enum CurveEditorMode
{
	Speed,
	Channels
}

[Flags]
public enum TransformCurveChannel
{
	None = 0,
	PositionX = 1 << 0,
	PositionY = 1 << 1,
	PositionZ = 1 << 2,
	RotationX = 1 << 3,
	RotationY = 1 << 4,
	RotationZ = 1 << 5,
	ScaleX = 1 << 6,
	ScaleY = 1 << 7,
	ScaleZ = 1 << 8,
	Position = PositionX | PositionY | PositionZ,
	Rotation = RotationX | RotationY | RotationZ,
	Scale = ScaleX | ScaleY | ScaleZ,
	All = Position | Rotation | Scale
}

public enum CurveHandleMode
{
	Aligned,
	Free
}

public enum AnimationTagKind
{
	Point,
	Range
}

public enum ReloadProfile
{
	Magazine,
	Incremental
}

public enum GripConfiguration
{
	OneHanded,
	TwoHanded
}

public enum AnchorKind
{
	Grip,
	RearBore,
	FrontBore,
	Muzzle,
	Eject,
	Custom
}

public enum RigControlKind
{
	Arm,
	Weapon,
	Camera
}

public enum VisibilityRenderMode
{
	BoneBranch,
	BodyGroup
}

public enum WeaponTextureChannel
{
	BaseColor,
	Normal,
	Roughness,
	Metalness,
	AmbientOcclusion,
	PackedOrm
}

public sealed class WeaponAnimationDocument
{
	public const int CurrentSchemaVersion = 4;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public Guid DocumentId { get; set; } = Guid.NewGuid();
	public string Name { get; set; } = "New Weapon";
	public WeaponAnimatorStage ActiveStage { get; set; } = WeaponAnimatorStage.Calibrate;
	public SourceModelSettings Source { get; set; } = new();
	public WeaponRigDefinition Rig { get; set; } = new();
	public WeaponCalibration Calibration { get; set; } = new();
	public ArmBindingDefinition Binding { get; set; } = new();
	public List<WeaponAnimationClip> Clips { get; set; } = [];
	public AnimGraphSettings Graph { get; set; } = new();
	public OutputSettings Output { get; set; } = new();
	public WorkspaceState Workspace { get; set; } = new();

	// Ownership is persisted beside generated assets. Keeping file-like strings out of the
	// GameResource prevents the asset compiler from treating manifest entries as dependencies.
	[JsonIgnore]
	public GenerationManifest Manifest { get; set; } = new();

	public static WeaponAnimationDocument CreateDefault( string name = "New Weapon" )
	{
		var document = new WeaponAnimationDocument
		{
			Name = name,
			Output = new OutputSettings
			{
				AssetName = Slugify( name )
			}
		};

		document.Clips = StandardClips()
			.Select( role => WeaponAnimationClip.Create( role ) )
			.ToList();

		document.Workspace.SelectedClipId = document.Clips
			.First( x => x.Role == WeaponClipRole.Idle ).Id;

		return document;
	}

	public WeaponAnimationClip? GetSelectedClip()
	{
		return Clips.FirstOrDefault( x => x.Id == Workspace.SelectedClipId )
			?? Clips.FirstOrDefault();
	}

	public WeaponAnimationClip EnsureClip( WeaponClipRole role )
	{
		var clip = Clips.FirstOrDefault( x => x.Role == role );
		if ( clip is not null )
			return clip;

		clip = WeaponAnimationClip.Create( role );
		Clips.Add( clip );
		return clip;
	}

	public static IReadOnlyList<WeaponClipRole> StandardClips() =>
	[
		WeaponClipRole.Idle,
		WeaponClipRole.Deploy,
		WeaponClipRole.Fire,
		WeaponClipRole.FireDry,
		WeaponClipRole.Reload,
		WeaponClipRole.ReloadEmpty,
		WeaponClipRole.Holster,
		WeaponClipRole.Inspect,
		WeaponClipRole.Sprint,
		WeaponClipRole.Jump,
		WeaponClipRole.Lower,
		WeaponClipRole.Ironsights,
		WeaponClipRole.GrabStance,
		WeaponClipRole.GrabGestureOne,
		WeaponClipRole.GrabGestureTwo,
		WeaponClipRole.GrabGestureThree,
		WeaponClipRole.GrabGestureFour,
		WeaponClipRole.ReloadEnter,
		WeaponClipRole.FirstShell,
		WeaponClipRole.InsertShell,
		WeaponClipRole.ReloadExit
	];

	public static string Slugify( string value )
	{
		if ( string.IsNullOrWhiteSpace( value ) )
			return "weapon";

		var chars = value.Trim().ToLowerInvariant()
			.Select( c => char.IsLetterOrDigit( c ) ? c : '_' )
			.ToArray();

		return string.Join( "_", new string( chars )
			.Split( '_', StringSplitOptions.RemoveEmptyEntries ) );
	}
}

public sealed class SourceModelSettings
{
	public string OriginalSourcePath { get; set; } = "";
	public string SourcePath { get; set; } = "";
	public string CompiledModelPath { get; set; } = "";
	public string PreviewHostPath { get; set; } = "";
	public string SourceHash { get; set; } = "";
	public string SourceRootBoneName { get; set; } = "";
	public bool NeedsModelDocWrapper { get; set; }
	public bool Compiled { get; set; }
	public bool PreviewHostCompiled { get; set; }
	public DateTime LastImportedUtc { get; set; }
	public List<SourceMaterialBinding> Materials { get; set; } = [];
}

public sealed class SourceMaterialBinding
{
	// Stored without a resource extension so the .wepanim compiler does not treat an
	// imported FBX slot label as a project asset dependency.
	public string SourceMaterialPath { get; set; } = "";
	public string Name { get; set; } = "";
	public string OutputName { get; set; } = "";

	[JsonIgnore]
	public string PreviewMaterialPath { get; set; } = "";
	public List<SourceTextureMap> Textures { get; set; } = [];

	public SourceTextureMap? FindTexture( WeaponTextureChannel channel ) =>
		Textures.FirstOrDefault( texture => texture.Channel == channel );

	public bool HasUsableTextures =>
		Textures.Any( texture => texture.Channel != WeaponTextureChannel.PackedOrm
			&& !string.IsNullOrWhiteSpace( texture.AssetPath ) );
}

public sealed class SourceTextureMap
{
	public WeaponTextureChannel Channel { get; set; }

	[JsonIgnore]
	public string OriginalPath { get; set; } = "";
	public string AssetPath { get; set; } = "";
	public string Sha256 { get; set; } = "";
}

public sealed class WeaponRigDefinition
{
	public string RootBone { get; set; } = "";
	public string SourceSkeletonRootId { get; set; } = "";
	public string WeaponSubtreeRootId { get; set; } = "";
	public List<WeaponBoneDefinition> Bones { get; set; } = [];
	public List<WeaponVisibilityPart> VisibilityParts { get; set; } = [];
	public List<RigAuditIssue> AuditIssues { get; set; } = [];
	public string ProfileHash { get; set; } = "";
	public bool ReviewRequired { get; set; }
	public bool FilteredPreviewConfirmed { get; set; }

	public WeaponBoneDefinition? FindBone( string idOrName ) =>
		Bones.FirstOrDefault( x =>
			string.Equals( x.Id, idOrName, StringComparison.OrdinalIgnoreCase )
			|| string.Equals( x.Name, idOrName, StringComparison.OrdinalIgnoreCase ) );

	public IEnumerable<WeaponBoneDefinition> RetainedBones() =>
		Bones.Where( x => x.Inclusion != WeaponBoneInclusion.Excluded
			&& x.Classification != WeaponBoneClassification.Ignored );
}

public sealed class WeaponVisibilityPart
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public string Name { get; set; } = "Visible Part";
	public string BoneId { get; set; } = "";
	public string BoneName { get; set; } = "";
	public bool DefaultVisible { get; set; } = true;
	public VisibilityRenderMode RenderMode { get; set; } = VisibilityRenderMode.BoneBranch;
	public string BodyGroupName { get; set; } = "";
	public int VisibleBodyGroupValue { get; set; } = 1;
	public int HiddenBodyGroupValue { get; set; }
}

public sealed class WeaponBoneDefinition
{
	public string Id { get; set; } = "";
	public string ParentId { get; set; } = "";
	public string HierarchyPath { get; set; } = "";
	public string Name { get; set; } = "";
	public string ParentName { get; set; } = "";
	public string OriginalName { get; set; } = "";
	public string OriginalParentName { get; set; } = "";
	public WeaponBoneClassification Classification { get; set; } = WeaponBoneClassification.Animatable;
	public WeaponBoneInclusion Inclusion { get; set; } = WeaponBoneInclusion.Included;

	// BindTransform is retained for loading version 2 projects.
	public Transform BindTransform { get; set; } = Transform.Zero;
	public Transform BindModelTransform { get; set; } = Transform.Zero;
	public Transform BindLocalTransform { get; set; } = Transform.Zero;
	public bool HasSkinInfluence { get; set; }
}

public sealed class RigAuditIssue
{
	public string Code { get; set; } = "";
	public string Message { get; set; } = "";
	public ValidationSeverity Severity { get; set; } = ValidationSeverity.Warning;
	public string BoneName { get; set; } = "";
}

public sealed class WeaponCalibration
{
	public float UniformScale { get; set; } = 1.0f;
	public Transform PhysicalTransform { get; set; } = Transform.Zero;
	public Transform FramingTransform { get; set; } = Transform.Zero;
	public ScaleMeasurement Measurement { get; set; } = new();
	public List<WeaponAnchor> Anchors { get; set; } = [];
	public WeaponUpAxis UpAxis { get; set; } = WeaponUpAxis.PositiveZ;
	public float HorizontalFov { get; set; } = 80.0f;
	public string AspectGuide { get; set; } = "16:9";
	public bool ShowSafeArea { get; set; } = true;
	public bool ShowCrosshair { get; set; } = true;
	public bool Confirmed { get; set; }
	public int Revision { get; set; }
	public CalibrationSnapshot? Snapshot { get; set; }

	public WeaponAnchor? GetAnchor( AnchorKind kind ) =>
		Anchors.FirstOrDefault( x => x.Kind == kind );

	public void SetAnchor( WeaponAnchor anchor )
	{
		var existing = GetAnchor( anchor.Kind );
		if ( existing is null )
			Anchors.Add( anchor );
		else
		{
			existing.Name = anchor.Name;
			existing.BoneName = anchor.BoneName;
			existing.LocalPosition = anchor.LocalPosition;
			existing.LocalRotation = anchor.LocalRotation;
		}
	}
}

public sealed class ScaleMeasurement
{
	public bool HasFirstPoint { get; set; }
	public bool HasSecondPoint { get; set; }
	public Vector3 FirstPoint { get; set; }
	public Vector3 SecondPoint { get; set; }
	public string FirstBone { get; set; } = "";
	public string SecondBone { get; set; } = "";
	public float KnownDistance { get; set; }
	public MeasurementUnit Unit { get; set; } = MeasurementUnit.Inches;
	public float PreviewScale { get; set; } = 1.0f;
	public bool HasPendingScale { get; set; }
	public Vector3 OriginalDimensions { get; set; }
	public Vector3 ResultingDimensions { get; set; }
}

public sealed class WeaponAnchor
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public string Name { get; set; } = "";
	public AnchorKind Kind { get; set; }
	public string BoneName { get; set; } = "";
	public Vector3 LocalPosition { get; set; }
	public Rotation LocalRotation { get; set; } = Rotation.Identity;
}

public sealed class CalibrationSnapshot
{
	public int Revision { get; set; }
	public string SourceHash { get; set; } = "";
	public string RigHash { get; set; } = "";
	public float UniformScale { get; set; } = 1.0f;
	public Transform PhysicalTransform { get; set; } = Transform.Zero;
	public Transform FramingTransform { get; set; } = Transform.Zero;
	public List<WeaponAnchor> Anchors { get; set; } = [];
	public DateTime ConfirmedUtc { get; set; }
}

public sealed class ArmBindingDefinition
{
	public string Profile { get; set; } = "FacepunchHumanV1";
	public string ArmsModel { get; set; } = "models/first_person/v_first_person_arms_human.vmdl";
	public GripConfiguration Configuration { get; set; } = GripConfiguration.TwoHanded;
	public RigTarget PrimaryHand { get; set; } = RigTarget.Create( "Primary Hand", true );
	public RigTarget SupportHand { get; set; } = RigTarget.Create( "Support Hand", false );
	public RigTarget PrimaryElbowPole { get; set; } = RigTarget.CreatePole( "Primary Elbow" );
	public RigTarget SupportElbowPole { get; set; } = RigTarget.CreatePole( "Support Elbow" );
	public List<GripPose> GripPoses { get; set; } = [];
	public Guid DefaultGripPoseId { get; set; }
	public bool ChecklistDismissed { get; set; }
	public List<string> CompletedChecklistItems { get; set; } = [];
}

public sealed class RigTarget
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public string Name { get; set; } = "";
	public RigControlKind Kind { get; set; } = RigControlKind.Arm;
	public Transform Transform { get; set; } = Transform.Zero;
	public string AttachedBone { get; set; } = "";
	public bool IsPrimary { get; set; }
	public bool IsBound { get; set; }
	public bool Reachable { get; set; } = true;

	public static RigTarget Create( string name, bool primary ) => new()
	{
		Name = name,
		IsPrimary = primary,
		Transform = new Transform( new Vector3( 12, primary ? -3 : 3, -2 ) )
	};

	public static RigTarget CreatePole( string name ) => new()
	{
		Name = name,
		Transform = new Transform( new Vector3( 5, name.Contains( "Primary" ) ? -12 : 12, -5 ) )
	};
}

public sealed class GripPose
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public string Name { get; set; } = "Default Grip";
	public List<BonePose> Bones { get; set; } = [];
}

public sealed class BonePose
{
	public string BoneName { get; set; } = "";
	public Transform LocalTransform { get; set; } = Transform.Zero;
}

public sealed class WeaponAnimationClip
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public string Name { get; set; } = "Custom";
	public WeaponClipRole Role { get; set; }
	// Generated Idle bind poses can follow calibration changes until deliberately authored.
	public bool IsBindPoseSeed { get; set; }
	public ClipReadiness Readiness { get; set; } = ClipReadiness.NotStarted;
	public float Duration { get; set; } = 1.0f;
	public float SampleRate { get; set; } = 30.0f;
	public bool AllowSubframeKeys { get; set; }
	public bool Loop { get; set; }
	public List<TransformTrack> Tracks { get; set; } = [];
	public List<VisibilityTrack> VisibilityTracks { get; set; } = [];
	public List<TimedConstraint> Constraints { get; set; } = [];
	public List<AnimationTag> Tags { get; set; } = [];
	public List<ClipParameterEvent> ParameterEvents { get; set; } = [];
	public string ImportedSequence { get; set; } = "";

	public static WeaponAnimationClip Create( WeaponClipRole role ) => new()
	{
		Name = WeaponAnimationNames.DisplayName( role ),
		Role = role,
		Loop = role is WeaponClipRole.Idle or WeaponClipRole.Sprint
	};

	public TransformTrack EnsureTrack( string target )
	{
		var track = Tracks.FirstOrDefault( x => x.Target == target );
		if ( track is not null )
			return track;

		track = new TransformTrack { Target = target };
		Tracks.Add( track );
		return track;
	}

	public VisibilityTrack EnsureVisibilityTrack( Guid partId )
	{
		var track = VisibilityTracks.FirstOrDefault( x => x.PartId == partId );
		if ( track is not null )
			return track;

		track = new VisibilityTrack { PartId = partId };
		VisibilityTracks.Add( track );
		return track;
	}
}

public sealed class VisibilityTrack
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid PartId { get; set; }
	public List<VisibilityKey> Keys { get; set; } = [];
	public bool Muted { get; set; }
}

public sealed class VisibilityKey
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public float Time { get; set; }
	public bool Visible { get; set; } = true;
}

public sealed class TransformTrack
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public string Target { get; set; } = "";
	public RigControlKind Kind { get; set; } = RigControlKind.Weapon;
	public TrackInterpolation Interpolation { get; set; } = TrackInterpolation.Cubic;
	public List<TransformKey> Keys { get; set; } = [];
	public List<TransformCurveSpan> CurveSpans { get; set; } = [];
	public bool Muted { get; set; }

	public TransformCurveSpan? FindCurveSpan( Guid startKeyId, Guid endKeyId ) =>
		CurveSpans.FirstOrDefault( x =>
			x.StartKeyId == startKeyId && x.EndKeyId == endKeyId );

	public TransformCurveSpan EnsureCurveSpan( Guid startKeyId, Guid endKeyId )
	{
		var span = FindCurveSpan( startKeyId, endKeyId );
		if ( span is not null )
			return span;

		span = new TransformCurveSpan
		{
			StartKeyId = startKeyId,
			EndKeyId = endKeyId
		};
		CurveSpans.Add( span );
		return span;
	}
}

public sealed class TransformKey
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public float Time { get; set; }
	public Vector3 Position { get; set; }
	public Rotation Rotation { get; set; } = Rotation.Identity;
	public Vector3 Scale { get; set; } = Vector3.One;
	// Retained for schema-v3 compatibility; migrated into CurveTangents.
	public Vector3 InTangent { get; set; }
	public Vector3 OutTangent { get; set; }
	public TransformCurveTangents CurveTangents { get; set; } = new();
}

public sealed class TransformCurveSpan
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid StartKeyId { get; set; }
	public Guid EndKeyId { get; set; }
	public bool HasSpeedCurve { get; set; }
	public MotionRateCurve Speed { get; set; } = new();
	public bool HasInterpolationOverride { get; set; }
	public TrackInterpolation Interpolation { get; set; } = TrackInterpolation.Linear;
	public TransformCurveChannel CustomChannels { get; set; }
}

public sealed class MotionRateCurve
{
	public float StartRate { get; set; } = 1.0f;
	public float EndRate { get; set; } = 1.0f;
	public float StartSlope { get; set; }
	public float EndSlope { get; set; }
	public CurveHandleMode StartHandleMode { get; set; } = CurveHandleMode.Aligned;
	public CurveHandleMode EndHandleMode { get; set; } = CurveHandleMode.Aligned;
}

public sealed class TransformCurveTangents
{
	public Vector3 PositionIn { get; set; }
	public Vector3 PositionOut { get; set; }
	public Vector3 RotationIn { get; set; }
	public Vector3 RotationOut { get; set; }
	public Vector3 ScaleIn { get; set; }
	public Vector3 ScaleOut { get; set; }
	public TransformCurveChannel FreeHandles { get; set; }
}

public sealed class TimedConstraint
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public string SourceControl { get; set; } = "";
	public string TargetBone { get; set; } = "";
	public float StartTime { get; set; }
	public float EndTime { get; set; } = 1.0f;
	public float Weight { get; set; } = 1.0f;
	public bool MaintainOffset { get; set; } = true;
}

public sealed class AnimationTag
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public string Name { get; set; } = "";
	public AnimationTagKind Kind { get; set; }
	public float StartTime { get; set; }
	public float EndTime { get; set; }
}

public sealed class ClipParameterEvent
{
	public string Name { get; set; } = "";
	public float Time { get; set; }
	public float Value { get; set; }
}

public sealed class AnimGraphSettings
{
	public bool GenerateGraph { get; set; } = true;
	public string ParameterProfile { get; set; } = "FacepunchHumanV1";
	public ReloadProfile ReloadProfile { get; set; } = ReloadProfile.Magazine;
	public bool FirearmProfile { get; set; } = true;
	public Dictionary<string, float> PreviewFloats { get; set; } = [];
	public Dictionary<string, bool> PreviewBools { get; set; } = [];
}

public sealed class OutputSettings
{
	public string AssetName { get; set; } = "weapon";
	public string OutputFolder { get; set; } = "";
	public bool GeneratePrefab { get; set; } = true;
	public bool GenerateGraph { get; set; } = true;
	public bool IncludeDebugSkeleton { get; set; }

	public string GetDefaultRelativeFolder()
	{
		var slug = WeaponAnimationDocument.Slugify( AssetName );
		return $"weapons/{slug}/viewmodel";
	}
}

public sealed class WorkspaceState
{
	public Guid SelectedClipId { get; set; }
	public float TimelineTime { get; set; }
	public string SelectedBone { get; set; } = "";
	public string SelectedControl { get; set; } = "";
	public string ConstraintTargetBone { get; set; } = "";
	public bool FirstPersonPreview { get; set; }
	public bool ShowGuides { get; set; }
	public bool ShowSkeleton { get; set; } = true;
	public bool ShowOnionSkins { get; set; }
	public float GridOpacity { get; set; } = 0.10f;
	public float GridLineThickness { get; set; } = 0.65f;
	public bool RimLightEnabled { get; set; } = true;
	public float RimLightIntensity { get; set; } = 4.0f;
	public bool AutoKey { get; set; } = true;
	public bool LocalGizmos { get; set; } = true;
	public bool SnapPosition { get; set; } = true;
	public bool SnapRotation { get; set; } = true;
	public float RotationSnapDegrees { get; set; } = 15.0f;
	public bool CurveEditorVisible { get; set; }
	public List<WorkingPoseOverride> WorkingPoseOverrides { get; set; } = [];
	public List<TimelineViewState> TimelineViews { get; set; } = [];
	public List<CurveViewState> CurveViews { get; set; } = [];
	public Vector3 CameraFocus { get; set; }
	public Angles CameraAngles { get; set; } = new( 12, 180, 0 );
	public float CameraDistance { get; set; } = 48.0f;
	public bool FreeLookCamera { get; set; }
	public Vector3 CameraPosition { get; set; }
	public float CameraMoveSpeed { get; set; } = 1.0f;
	public bool FullBrightViewport { get; set; }
	public string CalibrationSplitterState { get; set; } = "";
	public string AnimationSplitterState { get; set; } = "";
	public string CalibrationVerticalSplitterState { get; set; } = "";
	public string AnimationVerticalSplitterState { get; set; } = "";
	public string AnimationTimelineSplitterState { get; set; } = "";
	public string AnimationRightSplitterState { get; set; } = "";
	public string AnimationMainSplitterState { get; set; } = "";
	public string AnimationOuterSplitterState { get; set; } = "";

	public TimelineViewState? GetTimelineView( Guid clipId ) =>
		TimelineViews.FirstOrDefault( x => x.ClipId == clipId );

	public TimelineViewState EnsureTimelineView( Guid clipId, float duration )
	{
		var existing = GetTimelineView( clipId );
		if ( existing is not null )
			return existing;

		existing = new TimelineViewState
		{
			ClipId = clipId,
			VisibleEnd = MathF.Max( duration, 0 )
		};
		TimelineViews.Add( existing );
		return existing;
	}

	public CurveViewState? GetCurveView( Guid clipId ) =>
		CurveViews.FirstOrDefault( x => x.ClipId == clipId );

	public CurveViewState EnsureCurveView( Guid clipId )
	{
		var existing = GetCurveView( clipId );
		if ( existing is not null )
			return existing;

		existing = new CurveViewState { ClipId = clipId };
		CurveViews.Add( existing );
		return existing;
	}

	public WorkingPoseOverride? GetWorkingPose( Guid clipId, string target ) =>
		WorkingPoseOverrides.FirstOrDefault( x =>
			x.ClipId == clipId
			&& x.Target.Equals( target, StringComparison.OrdinalIgnoreCase ) );

	public void SetWorkingPose(
		Guid clipId,
		string target,
		RigControlKind kind,
		Transform transform )
	{
		var existing = GetWorkingPose( clipId, target );
		if ( existing is null )
		{
			WorkingPoseOverrides.Add( new WorkingPoseOverride
			{
				ClipId = clipId,
				Target = target,
				Kind = kind,
				Transform = transform
			} );
			return;
		}

		existing.Kind = kind;
		existing.Transform = transform;
	}

	public bool RemoveWorkingPose( Guid clipId, string target ) =>
		WorkingPoseOverrides.RemoveAll( x =>
			x.ClipId == clipId
			&& x.Target.Equals( target, StringComparison.OrdinalIgnoreCase ) ) > 0;

	public void ClearWorkingPoses( Guid clipId ) =>
		WorkingPoseOverrides.RemoveAll( x => x.ClipId == clipId );
}

public sealed class TimelineViewState
{
	public Guid ClipId { get; set; }
	public float VisibleStart { get; set; }
	public float VisibleEnd { get; set; }
	public float VerticalScroll { get; set; }
}

public sealed class CurveViewState
{
	public Guid ClipId { get; set; }
	public Guid SelectedTrackId { get; set; }
	public CurveEditorMode Mode { get; set; }
	public TransformCurveChannel VisibleChannels { get; set; }
	public string Search { get; set; } = "";
	public float TrackScroll { get; set; }
	public bool HasVerticalRange { get; set; }
	public float VerticalMinimum { get; set; }
	public float VerticalMaximum { get; set; } = 2.0f;
}

public sealed class WorkingPoseOverride
{
	public Guid ClipId { get; set; }
	public string Target { get; set; } = "";
	public RigControlKind Kind { get; set; }
	public Transform Transform { get; set; } = Transform.Zero;
}

public sealed class GenerationManifest
{
	public string GeneratorVersion { get; set; } = "";
	public DateTime GeneratedUtc { get; set; }
	public string InputHash { get; set; } = "";
	public List<GeneratedFileRecord> Files { get; set; } = [];
	public List<GenerationDiagnostic> Diagnostics { get; set; } = [];
}

public sealed class GeneratedFileRecord
{
	public string RelativePath { get; set; } = "";
	public string Sha256 { get; set; } = "";
	public string Kind { get; set; } = "";
}

public sealed class GenerationDiagnostic
{
	public ValidationSeverity Severity { get; set; }
	public string Code { get; set; } = "";
	public string Message { get; set; } = "";
	public string AssetPath { get; set; } = "";
}

public static class WeaponAnimationNames
{
	public static string DisplayName( WeaponClipRole role ) => role switch
	{
		WeaponClipRole.FireDry => "Fire Dry",
		WeaponClipRole.ReloadEmpty => "Reload Empty",
		WeaponClipRole.GrabStance => "Grab Stance",
		WeaponClipRole.GrabGestureOne => "Grab Gesture 1",
		WeaponClipRole.GrabGestureTwo => "Grab Gesture 2",
		WeaponClipRole.GrabGestureThree => "Grab Gesture 3",
		WeaponClipRole.GrabGestureFour => "Grab Gesture 4",
		WeaponClipRole.ReloadEnter => "Reload Enter",
		WeaponClipRole.FirstShell => "First Shell",
		WeaponClipRole.InsertShell => "Insert Shell",
		WeaponClipRole.ReloadExit => "Reload Exit",
		_ => role.ToString()
	};

	public static string SequenceName( WeaponClipRole role ) =>
		WeaponAnimationDocument.Slugify( DisplayName( role ) );

	public static string SequenceName( WeaponAnimationClip clip ) =>
		clip.Role == WeaponClipRole.Custom
			? $"{WeaponAnimationDocument.Slugify( clip.Name )}_{clip.Id:N}"
			: SequenceName( clip.Role );
}
