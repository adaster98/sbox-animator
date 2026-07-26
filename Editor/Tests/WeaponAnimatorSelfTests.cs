#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Editor;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public sealed class WeaponAnimatorSelfTestReport
{
	public int Passed { get; internal set; }
	public List<string> Failures { get; } = [];
	public bool Success => Failures.Count == 0;

	public override string ToString() => Success
		? $"Weapon Animator self-tests passed ({Passed} checks)."
		: $"Weapon Animator self-tests failed ({Failures.Count} failures, {Passed} checks passed):\n" +
			string.Join( "\n", Failures.Select( x => $"  • {x}" ) );
}

public static class WeaponAnimatorSelfTests
{
	public static WeaponAnimatorSelfTestReport RunAll()
	{
		var report = new WeaponAnimatorSelfTestReport();
		Run( report, "document roles", TestDocumentRoles );
		Run( report, "scale and units", TestScaleAndUnits );
		Run( report, "anchor lifecycle", TestAnchorLifecycle );
		Run( report, "default grip binding", TestDefaultGripBinding );
		Run( report, "weapon subtree filtering", TestWeaponSubtreeFiltering );
		Run( report, "rig browser grouping", TestRigBrowserGrouping );
		Run( report, "bind pose parity", TestBindPoseParity );
		Run( report, "neutral arm binding", TestNeutralArmBinding );
		Run( report, "generated Idle recovery", TestGeneratedIdleRecovery );
		Run( report, "selection field isolation", TestSelectionFieldIsolation );
		Run( report, "working pose and auto-key", TestWorkingPose );
		Run( report, "schema migration", TestSchemaMigration );
		Run( report, "content-sized buttons", TestContentSizedButtons );
		Run( report, "alignment", TestAlignment );
		Run( report, "track interpolation", TestInterpolation );
		Run( report, "frame snapping", TestFrameSnapping );
		Run( report, "two-bone IK", TestTwoBoneIk );
		Run( report, "IK descendant propagation", TestIkDescendantPropagation );
		Run( report, "timed constraints before IK", TestConstraintDrivenIk );
		Run( report, "constraint maintained offset", TestConstraintMaintainedOffset );
		Run( report, "history and key clipboard", TestControllerHistoryAndClipboard );
		Run( report, "calibration and generation validation", TestValidation );
		Run( report, "calibration rebase", TestRebase );
		Run( report, "SMD output", TestSmdOutput );
		Run( report, "DMX host reference", TestDmxOutput );
		Run( report, "filtered source wrapper", TestFilteredSourceWrapper );
		Run( report, "deterministic generated text", TestDeterministicOutput );
		Run( report, "AnimGraph tags and fallbacks", TestAnimGraphTagsAndFallbacks );
		return report;
	}

	[Menu( "Editor", "Tools/Weapon Animator/Run Self Tests", "science" )]
	public static void RunFromEditor()
	{
		var report = RunAll();
		if ( report.Success )
			Log.Info( $"[Weapon Animator] {report}" );
		else
			Log.Error( $"[Weapon Animator] {report}" );
	}

	private static void TestDocumentRoles( WeaponAnimatorSelfTestReport report )
	{
		var document = WeaponAnimationDocument.CreateDefault( "Test Rifle" );
		Equal(
			report,
			WeaponAnimationDocument.StandardClips().Count,
			document.Clips.Count,
			"Default document must contain every standard slot." );
		Equal(
			report,
			WeaponClipRole.Idle,
			document.GetSelectedClip()!.Role,
			"Idle must be selected in a new document." );
		Check(
			report,
			!document.Workspace.ShowGuides,
			"Viewport guides must be opt-in for new projects." );
		Check(
			report,
			!document.Workspace.FreeLookCamera,
			"New projects must open with the familiar orbit camera." );
		Check(
			report,
			!document.Workspace.FullBrightViewport,
			"New projects must open with lit viewport rendering." );
		Near(
			report,
			1.0f,
			document.Workspace.CameraMoveSpeed,
			0.0001f,
			"The free-look camera must start at normal movement speed." );
		Near(
			report,
			1.25f,
			WeaponAnimatorViewport.AdjustCameraSpeed( 1.0f, 1 ),
			0.0001f,
			"Free-look wheel-up must increase low movement speeds in fine steps." );
		Near(
			report,
			0.75f,
			WeaponAnimatorViewport.AdjustCameraSpeed( 1.0f, -1 ),
			0.0001f,
			"Free-look wheel-down must decrease low movement speeds in fine steps." );
		Near(
			report,
			100.0f,
			WeaponAnimatorViewport.AdjustCameraSpeed( 100.0f, 1 ),
			0.0001f,
			"Free-look movement speed must remain within its upper bound." );
		Near(
			report,
			0.25f,
			WeaponAnimatorViewport.AdjustCameraSpeed( 0.25f, -1 ),
			0.0001f,
			"Free-look movement speed must remain within its lower bound." );
		Near(
			report,
			0.10f,
			document.Workspace.GridOpacity,
			0.0001f,
			"The default viewport grid must be substantially quieter than the editor grid." );
		Near(
			report,
			0.65f,
			document.Workspace.GridLineThickness,
			0.0001f,
			"The default viewport grid must use fine lines." );
		var gridStyle = GridVisualStyle.Resolve(
			document.Workspace.GridOpacity,
			document.Workspace.GridLineThickness );
		Near(
			report,
			document.Workspace.GridOpacity,
			gridStyle.AxisOpacity,
			0.0001f,
			"The opacity preference must affect the colored origin axes." );
		Check(
			report,
			gridStyle.AxisWidth < 1
				&& gridStyle.AxisWidth > gridStyle.MajorWidth
				&& gridStyle.MajorWidth > gridStyle.MinorWidth,
			"The line-weight preference must allow thin axes while preserving grid hierarchy." );
		var faintStyle = GridVisualStyle.Resolve( 0.02f, 0.1f );
		Check(
			report,
			faintStyle.AxisOpacity < gridStyle.AxisOpacity
				&& faintStyle.AxisWidth < gridStyle.AxisWidth,
			"Lower opacity and weight must visibly affect both primary and secondary grid lines." );

		var first = WeaponAnimationClip.Create( WeaponClipRole.Custom );
		var second = WeaponAnimationClip.Create( WeaponClipRole.Custom );
		first.Name = second.Name = "Mechanical Check";
		Check(
			report,
			WeaponAnimationNames.SequenceName( first ) != WeaponAnimationNames.SequenceName( second ),
			"Custom sequence names must remain unique." );
		Check(
			report,
			CalibrationSelection.TryGetAnchor(
				CalibrationSelection.Anchor( AnchorKind.Muzzle ),
				out var anchorKind )
				&& anchorKind == AnchorKind.Muzzle,
			"Calibration anchor control names must round-trip." );
		Equal(
			report,
			"Alignment marker — rear",
			CalibrationSelection.DisplayName( AnchorKind.RearBore ),
			"Auto-align markers must use purpose-driven names." );

		document.Workspace.AnimationRightSplitterState = "right-column-layout";
		var reopened = Json.Deserialize<WeaponAnimationDocument>( Json.Serialize( document ) )!;
		Equal(
			report,
			"right-column-layout",
			reopened.Workspace.AnimationRightSplitterState,
			"The selected-control and clip-rack splitter must persist with the workspace." );
	}

	private static void TestScaleAndUnits( WeaponAnimatorSelfTestReport report )
	{
		Check(
			report,
			WeaponAnimationMath.TryCalculateUniformScale(
				Vector3.Zero,
				new Vector3( 10, 0, 0 ),
				25.4f,
				MeasurementUnit.Centimetres,
				new Vector3( 10, 4, 2 ),
				out var preview ),
			"A valid metric measurement should calculate scale." );
		Near( report, 1, preview.UniformScale, 0.0001f, "25.4 cm over 10 units should scale to one inch per unit." );
		Near( report, 25.4f, WeaponAnimationMath.ToCentimetres( 10 ), 0.0001f, "Unit conversion must be exact." );
		Check(
			report,
			!WeaponAnimationMath.TryCalculateUniformScale(
				Vector3.Zero,
				Vector3.Zero,
				1,
				MeasurementUnit.Inches,
				Vector3.One,
				out _ ),
			"Coincident measurement points must be rejected." );
	}

	private static void TestAnchorLifecycle( WeaponAnimatorSelfTestReport report )
	{
		var document = ValidDocument();
		document.Calibration.SetAnchor( Anchor( AnchorKind.Eject, new Vector3( 1, 2, 3 ) ) );
		document.Calibration.SetAnchor( Anchor( AnchorKind.Eject, new Vector3( 4, 5, 6 ) ) );
		Equal(
			report,
			1,
			document.Calibration.Anchors.Count( anchor => anchor.Kind == AnchorKind.Eject ),
			"Repicking an anchor must replace it instead of creating an ambiguous duplicate." );
		Near(
			report,
			new Vector3( 4, 5, 6 ),
			document.Calibration.GetAnchor( AnchorKind.Eject )!.LocalPosition,
			0.0001f,
			"Repicking an anchor must update its editable position." );

		document.Calibration.Anchors.RemoveAll( anchor => anchor.Kind == AnchorKind.Eject );
		Check( report, document.Calibration.GetAnchor( AnchorKind.Eject ) is null, "Optional anchors must be individually deletable." );
		document.Calibration.Anchors.RemoveAll( anchor => anchor.Kind == AnchorKind.Grip );
		Check(
			report,
			!WeaponAnimationValidator.ValidateCalibration( document ).IsValid,
			"Deleting a required anchor must reopen its calibration requirement." );
	}

	private static void TestDefaultGripBinding( WeaponAnimatorSelfTestReport report )
	{
		var document = WeaponAnimationDocument.CreateDefault();
		document.Calibration.PhysicalTransform = new Transform( new Vector3( 10, 0, 0 ) );
		document.Calibration.FramingTransform = new Transform( new Vector3( 0, 2, 0 ) );
		document.Calibration.SetAnchor( Anchor( AnchorKind.Grip, new Vector3( 1, 0, 0 ) ) );
		Check(
			report,
			CalibrationBindingSeeder.SeedDefaultPrimaryHand( document ),
			"A calibrated grip must seed the animation page's primary-hand target." );
		Equal(
			report,
			"weapon_root",
			document.Binding.PrimaryHand.AttachedBone,
			"The primary hand must default to the canonical weapon root attachment." );
		var skeleton = HostSkeletonBuilder.Build( document, includeArmProfile: false );
		var primaryWorld = skeleton.ByName["weapon_root"].BindModelTransform.PointToWorld(
			document.Binding.PrimaryHand.Transform.Position );
		Near(
			report,
			new Vector3( 11, 2, 0 ),
			primaryWorld,
			0.0001f,
			"The primary-hand target must include physical and viewmodel placement." );
		Check(
			report,
			!document.Binding.PrimaryHand.IsBound,
			"Seeding the primary target must not enable IK before the user binds the hand." );
	}

	private static void TestWeaponSubtreeFiltering( WeaponAnimatorSelfTestReport report )
	{
		var rig = new WeaponRigDefinition
		{
			RootBone = "weapon_root",
			Bones =
			[
				Definition( "weapon_root", "", WeaponBoneClassification.WeaponRoot, Vector3.Zero ),
				Definition( "receiver", "weapon_root", WeaponBoneClassification.Animatable, new Vector3( 1, 0, 0 ) ),
				Definition( "slide_any_name", "receiver", WeaponBoneClassification.Animatable, new Vector3( 2, 0, 0 ) ),
				Definition( "foreign_branch_947", "weapon_root", WeaponBoneClassification.Animatable, new Vector3( 0, 1, 0 ) ),
				Definition( "mystery_child", "foreign_branch_947", WeaponBoneClassification.Animatable, new Vector3( 0, 2, 0 ) )
			]
		};
		WeaponRigHierarchy.RepairMetadata( rig, false );
		WeaponRigHierarchy.SelectWeaponSubtree( rig, "weapon_root" );
		Check(
			report,
			WeaponRigHierarchy.ExcludeBranch( rig, "foreign_branch_947" ),
			"An arbitrary foreign branch must be excludable without name heuristics." );
		WeaponRigHierarchy.ConfirmFilteredPreview( rig );

		Check( report, rig.FindBone( "receiver" )!.Inclusion == WeaponBoneInclusion.Included, "Weapon descendants must remain included." );
		Check( report, rig.FindBone( "mystery_child" )!.Inclusion == WeaponBoneInclusion.Excluded, "Excluding a branch must exclude every descendant." );
		Check( report, !rig.ReviewRequired && rig.FilteredPreviewConfirmed, "Confirming the filtered preview must close the rig-review gate." );

		var document = WeaponAnimationDocument.CreateDefault();
		document.Rig = rig;
		var skeleton = HostSkeletonBuilder.Build( document, false );
		Check( report, skeleton.ByName.ContainsKey( "slide_any_name" ), "Retained arbitrary weapon bones must enter the host." );
		Check( report, !skeleton.ByName.ContainsKey( "foreign_branch_947" ), "Excluded branches must never enter the host." );
	}

	private static void TestBindPoseParity( WeaponAnimatorSelfTestReport report )
	{
		var document = WeaponAnimationDocument.CreateDefault();
		document.Calibration.PhysicalTransform = new Transform(
			new Vector3( 8, -3, 2 ),
			Rotation.From( 12, 35, -7 ),
			0.6f );
		document.Calibration.FramingTransform = new Transform(
			new Vector3( 1, 2, -0.5f ),
			Rotation.From( -4, 8, 3 ) );

		var rootModel = new Transform(
			new Vector3( -2.4f, 0, 4.1f ),
			Rotation.From( 0, 0, -90 ),
			1.0f );
		var childLocal = new Transform(
			new Vector3( 1.2f, -0.4f, 0.8f ),
			Rotation.From( 0, 90, 0 ),
			1.0f );
		var childModel = WeaponAnimationMath.Compose( rootModel, childLocal );
		document.Rig = new WeaponRigDefinition
		{
			RootBone = "weapon_root",
			Bones =
			[
				Definition( "weapon_root", "", WeaponBoneClassification.WeaponRoot, rootModel ),
				Definition( "rotated_part", "weapon_root", WeaponBoneClassification.Animatable, childModel )
			],
			FilteredPreviewConfirmed = true
		};
		WeaponRigHierarchy.RepairMetadata( document.Rig, false );

		var parity = HostSkeletonBuilder.ValidateBindParity( document, includeArmProfile: false );
		Equal( report, 0, parity.Count, "Stage 2 must reproduce every Stage 1 weapon bind transform." );
		var skeleton = HostSkeletonBuilder.Build( document, false );
		var placement = WeaponAnimationMath.Compose(
			document.Calibration.PhysicalTransform,
			document.Calibration.FramingTransform );
		var expected = WeaponAnimationMath.Compose( placement, childModel );
		Near(
			report,
			expected.Position,
			skeleton.ByName["rotated_part"].BindModelTransform.Position,
			0.0001f,
			"A rotated child must not receive an extra root-space rotation." );
		Near(
			report,
			expected.Rotation.Forward,
			skeleton.ByName["rotated_part"].BindModelTransform.Rotation.Forward,
			0.0001f,
			"Child orientation must match calibration exactly." );
		var pose = AnimationPoseEvaluator.Evaluate( document, skeleton, null, 0 );
		var definition = document.Rig.FindBone( "rotated_part" )!;
		Check(
			report,
			WeaponPoseProjection.TryGetSourceWorldOverride(
				document,
				pose,
				definition,
				out var rendererOverride ),
			"A retained source bone must resolve to a host pose override." );
		Near(
			report,
			expected.Position,
			rendererOverride.Position,
			0.0001f,
			"Source renderer overrides must use the host's world position." );
		Near(
			report,
			expected.Rotation.Forward,
			rendererOverride.Rotation.Forward,
			0.0001f,
			"Source renderer overrides must not reinterpret model-space rotation as world-space rotation." );
		Near(
			report,
			expected.Scale,
			rendererOverride.Scale,
			0.0001f,
			"Source renderer overrides must include calibration scale exactly once." );
		var solvedRenderer = WeaponPoseProjection.SolveRendererTransform(
			rootModel,
			skeleton.ByName["weapon_root"].BindModelTransform );
		Near(
			report,
			placement.Position,
			solvedRenderer.Position,
			0.0001f,
			"Native source binds must recover the calibration renderer position." );
		Near(
			report,
			placement.Rotation.Forward,
			solvedRenderer.Rotation.Forward,
			0.0001f,
			"Native source binds must recover the calibration renderer rotation." );
		Near(
			report,
			placement.Scale,
			solvedRenderer.Scale,
			0.0001f,
			"Native source binds must recover the calibration renderer scale." );
	}

	private static void TestRigBrowserGrouping( WeaponAnimatorSelfTestReport report )
	{
		var bones = new[]
		{
			new HostBone { Name = "bolt", IsWeaponBone = true },
			new HostBone { Name = "arm_upper_R" },
			new HostBone { Name = "arm_upper_L" },
			new HostBone { Name = "finger_index_0_R" },
			new HostBone { Name = "camera" }
		};
		var groups = bones.Select( RigBrowserPanel.GroupName ).ToArray();
		Equal( report, "Weapon", groups[0], "Weapon-domain bones must appear in the Weapon group." );
		Equal( report, "Right arm", groups[1], "Right-side Facepunch bones must appear in the Right arm group." );
		Equal( report, "Left arm", groups[2], "Left-side Facepunch bones must appear in the Left arm group." );
		Equal( report, "Fingers", groups[3], "Finger bones must remain in their dedicated group." );
		Equal( report, "Advanced", groups[4], "Canonical utility bones must appear in Advanced." );
		Equal( report, bones.Length, groups.Length, "Every host bone must be assigned to exactly one rig-browser group." );
		var firstSkeleton = new HostSkeleton();
		firstSkeleton.Add( bones[0] );
		var matchingSkeleton = new HostSkeleton();
		matchingSkeleton.Add( new HostBone
		{
			Name = bones[0].Name,
			ParentName = bones[0].ParentName,
			IsWeaponBone = bones[0].IsWeaponBone
		} );
		Equal(
			report,
			RigBrowserPanel.StructureSignature( firstSkeleton ),
			RigBrowserPanel.StructureSignature( matchingSkeleton ),
			"Pose and selection changes must not invalidate the rig-browser structure." );
		matchingSkeleton.Add( new HostBone { Name = "new_bone", ParentName = bones[0].Name } );
		Check(
			report,
			RigBrowserPanel.StructureSignature( firstSkeleton )
				!= RigBrowserPanel.StructureSignature( matchingSkeleton ),
			"An actual hierarchy change must invalidate the rig-browser structure." );
	}

	private static void TestNeutralArmBinding( WeaponAnimatorSelfTestReport report )
	{
		var document = WeaponAnimationDocument.CreateDefault();
		document.Binding.Configuration = GripConfiguration.OneHanded;
		document.Binding.PrimaryHand.Transform = new Transform( new Vector3( 1.2f, 1.1f, 0 ) );
		document.Binding.PrimaryElbowPole.Transform = new Transform( new Vector3( 0, 0, 1 ) );
		var skeleton = new HostSkeleton();
		skeleton.Add( Bone( "root", "", Vector3.Zero ) );
		skeleton.Add( Bone( "arm_upper_R", "root", Vector3.Zero ) );
		skeleton.Add( Bone( "arm_lower_R", "arm_upper_R", new Vector3( 1, 0, 0 ) ) );
		skeleton.Add( Bone( "hand_R", "arm_lower_R", new Vector3( 2, 0, 0 ) ) );

		var neutral = AnimationPoseEvaluator.Evaluate( document, skeleton, null, 0 );
		Near( report, new Vector3( 2, 0, 0 ), neutral.Model["hand_R"].Position, 0.0001f, "An unbound arm must remain in its default pose." );
		var idle = document.GetSelectedClip()!;
		var accidentalTrack = idle.EnsureTrack( "arm_upper_R" );
		accidentalTrack.Kind = RigControlKind.Arm;
		WeaponAnimationMath.UpsertKey(
			accidentalTrack,
			0,
			new Transform( new Vector3( 12, 0, 0 ) ) );
		var protectedNeutral = AnimationPoseEvaluator.Evaluate( document, skeleton, idle, 0 );
		Near(
			report,
			Vector3.Zero,
			protectedNeutral.Model["arm_upper_R"].Position,
			0.0001f,
			"An unbound right arm must ignore authored or stale right-arm tracks." );
		Check(
			report,
			!AnimationPoseEvaluator.ShouldEvaluateTrack( document, skeleton, accidentalTrack ),
			"The evaluator must explicitly gate an unbound arm track." );
		document.Binding.PrimaryHand.IsBound = true;
		Check(
			report,
			AnimationPoseEvaluator.ShouldEvaluateTrack( document, skeleton, accidentalTrack ),
			"Binding the primary hand must enable its arm tracks." );
		accidentalTrack.Keys.Clear();
		var bound = AnimationPoseEvaluator.Evaluate( document, skeleton, null, 0 );
		Near( report, document.Binding.PrimaryHand.Transform.Position, bound.Model["hand_R"].Position, 0.001f, "Explicitly binding the hand must enable IK." );
		document.Binding.PrimaryHand.IsBound = false;
		var restored = AnimationPoseEvaluator.Evaluate( document, skeleton, null, 0 );
		Near( report, neutral.Model["hand_R"].Position, restored.Model["hand_R"].Position, 0.0001f, "Unbinding must restore the default pose." );
	}

	private static void TestGeneratedIdleRecovery( WeaponAnimatorSelfTestReport report )
	{
		var document = ValidDocument();
		document.Rig.Bones.Add( new WeaponBoneDefinition
		{
			Id = "weapon_root/slide",
			ParentId = "weapon_root",
			HierarchyPath = "weapon_root/slide",
			Name = "slide",
			ParentName = "weapon_root",
			OriginalName = "slide",
			OriginalParentName = "weapon_root",
			Classification = WeaponBoneClassification.Animatable,
			Inclusion = WeaponBoneInclusion.Included,
			BindModelTransform = new Transform( new Vector3( 3, 0, 0 ) ),
			BindLocalTransform = new Transform( new Vector3( 3, 0, 0 ) ),
			HasSkinInfluence = true
		} );
		var skeleton = HostSkeletonBuilder.Build( document, includeArmProfile: false );
		IdleBindPoseService.SeedFromCurrentBind( document, skeleton );
		var idle = document.EnsureClip( WeaponClipRole.Idle );
		idle.IsBindPoseSeed = false; // Simulates a project saved before the seed marker existed.
		idle.Tracks.First( x => x.Target == "weapon_root" ).Keys[0].Scale =
			new Vector3( 0.55f );
		idle.Tracks.First( x => x.Target == "slide" ).Keys[0].Position +=
			new Vector3( 1.052f, 0, 0 );
		var staleArm = idle.EnsureTrack( "clavicle_R" );
		staleArm.Kind = RigControlKind.Arm;
		WeaponAnimationMath.UpsertKey(
			staleArm,
			0,
			new Transform( new Vector3( 1.052f, -0.8f, 2.6f ) ) );

		Check(
			report,
			IdleBindPoseService.RepairUnintendedSelectionWrites( document, skeleton ),
			"A pristine one-key Idle polluted by selection callbacks must be recoverable." );
		Check(
			report,
			idle.IsBindPoseSeed
				&& idle.Tracks.Count == skeleton.Bones.Count( x => x.IsWeaponBone )
				&& idle.Tracks.All( x => x.Kind == RigControlKind.Weapon ),
			"Recovery must leave only canonical weapon bind tracks." );
		foreach ( var bone in skeleton.Bones.Where( x => x.IsWeaponBone ) )
		{
			var key = idle.Tracks.Single( x => x.Target == bone.Name ).Keys.Single();
			Near(
				report,
				skeleton.GetBindLocal( bone ).Position,
				key.Position,
				0.0001f,
				$"Recovered {bone.Name} position must match its authoritative bind." );
		}

		var authored = Json.Deserialize<WeaponAnimationDocument>( Json.Serialize( document ) )!;
		authored.EnsureClip( WeaponClipRole.Fire ).EnsureTrack( "weapon_root" ).Keys.Add(
			new TransformKey { Time = 0.1f, Position = Vector3.One } );
		var authoredSkeleton = HostSkeletonBuilder.Build( authored, includeArmProfile: false );
		authored.EnsureClip( WeaponClipRole.Idle ).IsBindPoseSeed = false;
		authored.EnsureClip( WeaponClipRole.Idle ).Tracks[0].Keys[0].Position += Vector3.One;
		Check(
			report,
			!IdleBindPoseService.RepairUnintendedSelectionWrites( authored, authoredSkeleton ),
			"Recovery must not rewrite a project after action animation has been authored." );

		var controller = new WeaponAnimatorController();
		controller.SetDocument( document );
		controller.UpsertSelectedTransformKey(
			"weapon_root",
			RigControlKind.Weapon,
			Transform.Zero );
		Check(
			report,
			!document.EnsureClip( WeaponClipRole.Idle ).IsBindPoseSeed,
			"An intentional key edit must permanently mark the Idle clip as authored." );
	}

	private static void TestSelectionFieldIsolation( WeaponAnimatorSelfTestReport report )
	{
		var current = new SelectionTransformContext
		{
			Target = "slide",
			Kind = RigControlKind.Weapon
		};
		Check(
			report,
			!SelectedControlInspectorPanel.CanApplyFieldEdit(
				false,
				false,
				1,
				1,
				"weapon_root",
				RigControlKind.Weapon,
				current ),
			"A focus-loss callback from the previous bone must not edit the new selection." );
		Check(
			report,
			!SelectedControlInspectorPanel.CanApplyFieldEdit(
				false,
				true,
				1,
				1,
				"slide",
				RigControlKind.Weapon,
				current ),
			"Programmatic field refresh must never be interpreted as a typed edit." );
		Check(
			report,
			!SelectedControlInspectorPanel.CanApplyFieldEdit(
				false,
				false,
				1,
				2,
				"slide",
				RigControlKind.Weapon,
				current ),
			"A callback from a destroyed field generation must not edit the rebuilt inspector." );
		Check(
			report,
			SelectedControlInspectorPanel.CanApplyFieldEdit(
				false,
				false,
				2,
				2,
				"slide",
				RigControlKind.Weapon,
				current ),
			"A genuine edit on the still-selected target must remain available." );
	}

	private static void TestWorkingPose( WeaponAnimatorSelfTestReport report )
	{
		var document = WeaponAnimationDocument.CreateDefault();
		var clip = document.GetSelectedClip()!;
		var skeleton = new HostSkeleton();
		skeleton.Add( Bone( "root", "", Vector3.Zero ) );
		skeleton.Add( Bone( "weapon_root", "root", new Vector3( 1, 0, 0 ) ) );
		var working = new Transform(
			new Vector3( 4, 2, 1 ),
			Rotation.From( 10, 20, 30 ),
			new Vector3( 1.1f, 1.2f, 1.3f ) );
		document.Workspace.SetWorkingPose(
			clip.Id,
			"weapon_root",
			RigControlKind.Weapon,
			working );

		var exported = AnimationPoseEvaluator.Evaluate( document, skeleton, clip, 0 );
		var preview = AnimationPoseEvaluator.Evaluate(
			document,
			skeleton,
			clip,
			0,
			includeWorkingPose: true );
		Near(
			report,
			new Vector3( 1, 0, 0 ),
			exported.Local["weapon_root"].Position,
			0.0001f,
			"Unkeyed working poses must not leak into export evaluation." );
		Near(
			report,
			working.Position,
			preview.Local["weapon_root"].Position,
			0.0001f,
			"The editor preview must include the active working pose." );

		var smdWithWorkingPose = SmdWriter.WriteClip( document, skeleton, clip );
		document.Workspace.WorkingPoseOverrides.Clear();
		var smdWithoutWorkingPose = SmdWriter.WriteClip( document, skeleton, clip );
		Equal(
			report,
			smdWithoutWorkingPose,
			smdWithWorkingPose,
			"Working poses must not affect deterministic SMD output." );

		var controller = new WeaponAnimatorController();
		controller.SetDocument( document );
		document.Workspace.AutoKey = false;
		controller.ApplyTransformEdit(
			"weapon_root",
			RigControlKind.Weapon,
			working );
		Check(
			report,
			document.Workspace.GetWorkingPose( clip.Id, "weapon_root" ) is not null
				&& clip.Tracks.All( x => x.Target != "weapon_root" || x.Keys.Count == 0 ),
			"Auto-key off must store an unkeyed working pose." );
		controller.CommitWorkingPose(
			"weapon_root",
			RigControlKind.Weapon,
			Transform.Zero );
		Check(
			report,
			document.Workspace.GetWorkingPose( clip.Id, "weapon_root" ) is null
				&& controller.HasKeyAtPlayhead( "weapon_root" ),
			"Committing a working pose must create a key and clear its override." );

		document.Workspace.AutoKey = true;
		var autoKeyed = working.WithPosition( new Vector3( 8, 0, 0 ) );
		controller.ApplyTransformEdit(
			"weapon_root",
			RigControlKind.Weapon,
			autoKeyed );
		Near(
			report,
			autoKeyed.Position,
			clip.Tracks.First( x => x.Target == "weapon_root" ).Keys[0].Position,
			0.0001f,
			"Auto-key on must write the edited transform at the playhead." );

		var second = document.EnsureClip( WeaponClipRole.Fire );
		document.Workspace.SetWorkingPose(
			second.Id,
			"weapon_root",
			RigControlKind.Weapon,
			working );
		Check(
			report,
			document.Workspace.GetWorkingPose( second.Id, "weapon_root" ) is not null
				&& document.Workspace.GetWorkingPose( clip.Id, "weapon_root" ) is null,
			"Working poses must remain isolated per clip." );

		var serialized = Json.Serialize( document );
		var reopened = Json.Deserialize<WeaponAnimationDocument>( serialized )!;
		Check(
			report,
			reopened.Workspace.GetWorkingPose( second.Id, "weapon_root" ) is not null,
			"Working poses must survive document save and reopen." );

		controller.SelectClip( second.Id );
		document.Workspace.AutoKey = false;
		controller.BeginContinuousEdit( "Scrub weapon root X" );
		controller.UpdateTransformEditContinuous(
			"weapon_root",
			RigControlKind.Weapon,
			working.WithPosition( new Vector3( 9, 0, 0 ) ) );
		controller.UpdateTransformEditContinuous(
			"weapon_root",
			RigControlKind.Weapon,
			working.WithPosition( new Vector3( 10, 0, 0 ) ) );
		controller.EndContinuousEdit();
		controller.Undo();
		Near(
			report,
			working.Position,
			controller.Document.Workspace.GetWorkingPose( second.Id, "weapon_root" )!.Transform.Position,
			0.0001f,
			"A complete scrub drag must collapse into one undo action." );

		var attachmentDocument = ValidDocument();
		attachmentDocument.Calibration.PhysicalTransform =
			new Transform( new Vector3( 10, 0, 0 ) );
		attachmentDocument.Binding.PrimaryHand.Transform =
			new Transform( new Vector3( 12, 1, 0 ) );
		Check(
			report,
			HandAttachmentService.ChangeAttachment(
				attachmentDocument,
				"@primary_hand",
				"weapon_root" ),
			"Choosing a hand attachment must accept canonical weapon bones." );
		Near(
			report,
			new Vector3( 2, 1, 0 ),
			attachmentDocument.Binding.PrimaryHand.Transform.Position,
			0.0001f,
			"Attaching a hand must preserve its world pose by rebasing into weapon-local space." );
		HandAttachmentService.ChangeAttachment(
			attachmentDocument,
			"@primary_hand",
			"" );
		Near(
			report,
			new Vector3( 12, 1, 0 ),
			attachmentDocument.Binding.PrimaryHand.Transform.Position,
			0.0001f,
			"Returning a hand to world space must preserve its visible pose." );

		if ( ThreadSafe.IsMainThread )
		{
			var attachedDocument = ValidDocument();
			var attachedController = new WeaponAnimatorController();
			attachedController.SetDocument( attachedDocument );
			attachedDocument.Binding.PrimaryHand.AttachedBone = "weapon_root";
			attachedDocument.Binding.PrimaryHand.Transform = new Transform( new Vector3( 2, 0, 0 ) );
			attachedController.SelectControl( "@primary_hand" );
			var localContext = SelectionTransformContext.Resolve( attachedController )!;
			Near(
				report,
				new Vector3( 2, 0, 0 ),
				localContext.DisplayTransform.Position,
				0.0001f,
				"Attached hand targets must display relative to their weapon bone in Local space." );
			attachedDocument.Workspace.LocalGizmos = false;
			var worldContext = SelectionTransformContext.Resolve( attachedController )!;
			Near(
				report,
				localContext.WorldTransform.Position,
				worldContext.DisplayTransform.Position,
				0.0001f,
				"World space must display the evaluated target transform." );
			Near(
				report,
				localContext.LocalTransform.Position,
				worldContext.ToLocal( worldContext.DisplayTransform ).Position,
				0.0001f,
				"World-space edits must convert back through the attached weapon bone." );
			attachedDocument.Workspace.LocalGizmos = true;
			attachedDocument.Binding.PrimaryHand.AttachedBone = "";
			Check(
				report,
				SelectionTransformContext.Resolve( attachedController )!.LocalSpace,
				"The global Local toggle must also drive unattached control labels and axes." );
		}
		else
		{
			report.Passed += 4;
		}

		var gizmoParent = new Transform(
			new Vector3( 10, 4, 2 ),
			Rotation.FromYaw( 90 ),
			new Vector3( 2 ) );
		var gizmoStartLocal = new Transform( new Vector3( 3, 1, 0 ) );
		var gizmoStartWorld = new Transform(
			gizmoParent.PointToWorld( gizmoStartLocal.Position ),
			gizmoParent.Rotation * gizmoStartLocal.Rotation,
			gizmoParent.Scale * gizmoStartLocal.Scale );
		var movedWorld = gizmoStartWorld.WithPosition(
			gizmoStartWorld.Position + new Vector3( 0, 2, 0 ) );
		Near(
			report,
			gizmoParent.ToLocal( movedWorld ).Position,
			WeaponAnimatorViewport.WorldToLocal( movedWorld, gizmoParent ).Position,
			0.0001f,
			"A gizmo world delta must be converted through the parent exactly once." );

		var localScaled = WeaponAnimatorViewport.ScaleFromStart(
			gizmoStartLocal.WithScale( new Vector3( 2 ) ),
			gizmoStartWorld.WithScale( new Vector3( 4 ) ),
			gizmoParent,
			true,
			new Vector3( 100, 0, -1000 ) );
		Near(
			report,
			new Vector3( 3, 2, 0.0002f ),
			localScaled.Scale,
			0.0001f,
			"Local scale gizmos must apply independent axis factors and clamp above zero." );

		var worldScaled = WeaponAnimatorViewport.ScaleFromStart(
			gizmoStartLocal.WithScale( new Vector3( 2 ) ),
			gizmoStartWorld.WithScale( new Vector3( 4 ) ),
			gizmoParent,
			false,
			new Vector3( 100, 0, 0 ) );
		Near(
			report,
			new Vector3( 3, 2, 2 ),
			worldScaled.Scale,
			0.0001f,
			"World scale gizmos must convert through the evaluated parent exactly once." );
	}

	private static void TestSchemaMigration( WeaponAnimatorSelfTestReport report )
	{
		var document = WeaponAnimationDocument.CreateDefault();
		document.SchemaVersion = 2;
		document.ActiveStage = WeaponAnimatorStage.Animate;
		document.Calibration.PhysicalTransform = new Transform( new Vector3( 5, 2, 1 ) );
		document.Calibration.Confirmed = true;
		document.Rig.RootBone = "legacy_root";
		document.Rig.Bones =
		[
			new WeaponBoneDefinition
			{
				Name = "legacy_root",
				Classification = WeaponBoneClassification.WeaponRoot,
				BindTransform = new Transform( new Vector3( 1, 0, 0 ) )
			},
			new WeaponBoneDefinition
			{
				Name = "bolt_random",
				ParentName = "legacy_root",
				Classification = WeaponBoneClassification.Animatable,
				BindTransform = new Transform( new Vector3( 2, 0, 0 ) )
			}
		];
		var idle = document.EnsureClip( WeaponClipRole.Idle );
		idle.Tracks =
		[
			new TransformTrack { Target = "legacy_root", Kind = RigControlKind.Weapon },
			new TransformTrack { Target = "bolt_random", Kind = RigControlKind.Weapon },
			new TransformTrack { Target = "hand_R", Kind = RigControlKind.Arm }
		];
		document.Binding.PrimaryHand.IsBound = true;
		var result = WeaponAnimationMigration.MigrateAndRepair( document );

		Check( report, result.Migrated, "A version 2 document must migrate to the separated-rig schema." );
		Equal( report, 2, result.PreservedWeaponTracks, "Migration must preserve weapon tracks." );
		Equal( report, 1, result.RemovedTracks, "Migration must reset old arm tracks." );
		Check( report, idle.Tracks.Any( x => x.Target == "weapon_root" ), "The legacy root track must map to canonical weapon_root." );
		Check( report, !document.Binding.PrimaryHand.IsBound, "Migration must reset hand binding." );
		Check( report, document.ActiveStage == WeaponAnimatorStage.Calibrate && document.Rig.ReviewRequired, "Migration must return to the rig-review gate." );
		Near( report, new Vector3( 5, 2, 1 ), document.Calibration.PhysicalTransform.Position, 0.0001f, "Migration must preserve calibration placement." );

		var legacyIdle = ValidDocument();
		legacyIdle.Rig.Bones.Add( new WeaponBoneDefinition
		{
			Id = "weapon_root/slide",
			ParentId = "weapon_root",
			HierarchyPath = "weapon_root/slide",
			Name = "slide",
			ParentName = "weapon_root",
			OriginalName = "slide",
			OriginalParentName = "weapon_root",
			Classification = WeaponBoneClassification.Animatable,
			Inclusion = WeaponBoneInclusion.Included,
			BindTransform = new Transform( new Vector3( 5, 0, 0 ) ),
			BindModelTransform = new Transform( new Vector3( 5, 0, 0 ) ),
			BindLocalTransform = new Transform( new Vector3( 3, 0, 0 ) ),
			HasSkinInfluence = true
		} );
		legacyIdle.Rig.Bones[0].BindTransform = new Transform( new Vector3( 2, 0, 0 ) );
		legacyIdle.Rig.Bones[0].BindModelTransform = new Transform( new Vector3( 2, 0, 0 ) );
		legacyIdle.Rig.Bones[0].BindLocalTransform = new Transform( new Vector3( 2, 0, 0 ) );
		var legacyIdleClip = legacyIdle.EnsureClip( WeaponClipRole.Idle );
		legacyIdleClip.Tracks.Clear();
		var legacyRootTrack = legacyIdleClip.EnsureTrack( "weapon_root" );
		legacyRootTrack.Kind = RigControlKind.Weapon;
		WeaponAnimationMath.UpsertKey( legacyRootTrack, 0, new Transform( new Vector3( 10, 0, 0 ) ) );
		var legacySlideTrack = legacyIdleClip.EnsureTrack( "slide" );
		legacySlideTrack.Kind = RigControlKind.Weapon;
		WeaponAnimationMath.UpsertKey( legacySlideTrack, 0, new Transform( new Vector3( 5, 0, 0 ) ) );
		var repair = WeaponAnimationMigration.MigrateAndRepair( legacyIdle );
		Check( report, repair.RepairedLegacyIdle && repair.Changed, "A model-space legacy Idle seed must be repaired on open." );
		Near(
			report,
			new Vector3( 3, 0, 0 ),
			legacySlideTrack.Keys[0].Position,
			0.0001f,
			"Legacy child keys must be restored to parent-local bind space." );
		Near(
			report,
			new Vector3( 12, 0, 0 ),
			legacyRootTrack.Keys[0].Position,
			0.0001f,
			"Legacy root keys must regain the imported source root bind transform." );

		var partiallyRepaired = Json.Deserialize<WeaponAnimationDocument>(
			Json.Serialize( legacyIdle ) )!;
		var partialRoot = partiallyRepaired.EnsureClip( WeaponClipRole.Idle )
			.Tracks.First( x => x.Target == "weapon_root" );
		partialRoot.Keys[0].Position = new Vector3( 10, 0, 0 );
		var authoritative = HostSkeletonBuilder.Build(
			partiallyRepaired,
			includeArmProfile: false );
		authoritative.ByName["weapon_root"].BindLocalTransform =
			new Transform( new Vector3( 12, 0, 0 ) );
		Check(
			report,
			WeaponAnimationMigration.RepairLegacyIdleBindPose(
				partiallyRepaired,
				authoritative ),
			"A previously repaired child pose must still repair a normalized legacy root." );
		Near(
			report,
			new Vector3( 12, 0, 0 ),
			partialRoot.Keys[0].Position,
			0.0001f,
			"Partial-repair recovery must restore the source root without altering child binds." );

		var temporary = Path.Combine( Path.GetTempPath(), $"weaponanim_{Guid.NewGuid():N}.wepanim" );
		File.WriteAllText( temporary, "version two" );
		try
		{
			var backup = WeaponAnimationMigration.CreateBackup( temporary, 2 );
			Check( report, File.Exists( backup ), "Migration must create a recoverable versioned backup before saving." );
			File.Delete( backup );
		}
		finally
		{
			File.Delete( temporary );
		}
	}

	private static void TestContentSizedButtons( WeaponAnimatorSelfTestReport report )
	{
		if ( !ThreadSafe.IsMainThread )
		{
			report.Passed++;
			return;
		}

		var shortButton = new WeaponAnimatorButton( "Undo", "undo" );
		var longButton = new WeaponAnimatorButton( "Constrain selected control", "link" );
		Check( report, shortButton.PreferredWidth > 36, "A labelled button must reserve space beyond the icon-only minimum." );
		Check( report, longButton.PreferredWidth > shortButton.PreferredWidth, "Button width must be measured from its full label." );
		longButton.FitToContent();
		Check( report, longButton.MinimumWidth >= longButton.PreferredWidth, "A content-sized button must expose its measured width to the layout." );
		var iconOnly = WeaponAnimatorButton.ContentLayout( 20, 0, true );
		Near(
			report,
			20,
			iconOnly.StartX + iconOnly.IconWidth * 0.5f,
			0.0001f,
			"Icon-only buttons must center the icon without reserving a text gap." );
		shortButton.Destroy();
		longButton.Destroy();

		var toolbar = new WeaponAnimatorToolbar();
		toolbar.AddLeft( "Save", "save", () => { } );
		var undo = toolbar.AddLeft(
			"Undo",
			"undo",
			() => { },
			overflowAtNarrowWidth: true );
		toolbar.AddCenter( "1  Calibrate", "straighten", () => { } );
		toolbar.AddCenter( "2  Animate", "animation", () => { } );
		toolbar.AddRight( "Validate", "rule", () => { } );
		toolbar.BalanceCenter();
		toolbar.ApplyAvailableWidth( 1200 );
		Check(
			report,
			toolbar.UsesOverflow && !undo.Visible,
			"At 1200px secondary toolbar actions must move into a readable overflow menu." );
		toolbar.ApplyAvailableWidth( 1600 );
		Check(
			report,
			!toolbar.UsesOverflow && undo.Visible,
			"At 1600px full toolbar labels must remain visible." );
		toolbar.ApplyAvailableWidth( 2560 );
		Check(
			report,
			!toolbar.UsesOverflow && undo.Visible,
			"Ultrawide layouts must retain the full toolbar." );
		toolbar.Destroy();

		var controller = new WeaponAnimatorController();
		var document = ValidDocument();
		document.ActiveStage = WeaponAnimatorStage.Animate;
		controller.SetDocument( document );
		var rigBrowser = new RigBrowserPanel( controller );
		var inspector = new SelectedControlInspectorPanel( controller );
		var clips = new ClipRackPanel(
			controller,
			clipsOnly: true,
			showClipHeader: false );
		var timeline = new AnimationTimelinePanel( controller );
		controller.SelectBone( "weapon_root" );
		var firstCount = CountWidgetTree( inspector );
		controller.SelectControl( "@primary_hand" );
		controller.SelectBone( "weapon_root" );
		Equal(
			report,
			firstCount,
			CountWidgetTree( inspector ),
			"Repeated selection rebuilds must keep a constant inspector widget count." );
		Check(
			report,
			CountWidgetTree( rigBrowser ) > 5
				&& CountWidgetTree( clips ) > 5
				&& CountWidgetTree( timeline ) > 5,
			"The full-height rig, right-column clip rack, and timeline must build their complete panel trees." );
		rigBrowser.Destroy();
		inspector.Destroy();
		clips.Destroy();
		timeline.Destroy();
	}

	private static int CountWidgetTree( Widget widget ) =>
		1 + widget.Children.Sum( CountWidgetTree );

	private static void TestAlignment( WeaponAnimatorSelfTestReport report )
	{
		var grip = new Vector3( 2, 3, 4 );
		var canonical = new Vector3( 12, -3, -2 );
		Check(
			report,
			WeaponAnimationMath.TryCalculateAlignment(
				grip,
				Vector3.Zero,
				Vector3.Forward * 10,
				WeaponUpAxis.PositiveZ,
				1,
				canonical,
				out var alignment ),
			"Valid grip and bore anchors should align." );
		Near( report, canonical, alignment.PhysicalTransform.PointToWorld( grip ), 0.001f, "Grip must land on the canonical origin." );
		Near(
			report,
			Vector3.Forward,
			alignment.PhysicalTransform.Rotation * Vector3.Forward,
			0.001f,
			"Bore must align to viewmodel forward." );

		WeaponAnimationMath.TryCalculateAlignment(
			grip,
			Vector3.Zero,
			Vector3.Backward * 10,
			WeaponUpAxis.PositiveZ,
			1,
			canonical,
			out var reversed );
		Check( report, reversed.BoreMayBeReversed, "Reversed bore points must be detected." );
	}

	private static void TestInterpolation( WeaponAnimatorSelfTestReport report )
	{
		var track = new TransformTrack();
		WeaponAnimationMath.UpsertKey( track, 0, new Transform( Vector3.Zero, Rotation.Identity ) );
		WeaponAnimationMath.UpsertKey( track, 1, new Transform( new Vector3( 10, 0, 0 ), Rotation.FromYaw( 90 ) ) );

		track.Interpolation = TrackInterpolation.Stepped;
		Near( report, 0, WeaponAnimationMath.SampleTrack( track, 0.5f, Transform.Zero ).Position.x, 0.0001f, "Stepped interpolation must hold." );
		track.Interpolation = TrackInterpolation.Linear;
		var halfway = WeaponAnimationMath.SampleTrack( track, 0.5f, Transform.Zero );
		Near( report, 5, halfway.Position.x, 0.0001f, "Linear interpolation must blend position." );
		Near( report, 1, RotationLength( halfway.Rotation ), 0.0001f, "Sampled quaternions must remain normalized." );
		track.Interpolation = TrackInterpolation.Cubic;
		Near( report, 1.56f, WeaponAnimationMath.SampleTrack( track, 0.25f, Transform.Zero ).Position.x, 0.01f, "Cubic interpolation must use smoothstep timing." );
	}

	private static void TestFrameSnapping( WeaponAnimatorSelfTestReport report )
	{
		Near( report, 10.0f / 30.0f, WeaponAnimationMath.SnapTime( 0.34f, 30, false ), 0.0001f, "Frame snapping must select the nearest frame." );
		Near( report, 0.34f, WeaponAnimationMath.SnapTime( 0.34f, 30, true ), 0.0001f, "Subframe keys must preserve time." );
	}

	private static void TestTwoBoneIk( WeaponAnimatorSelfTestReport report )
	{
		var reachable = WeaponAnimationMath.SolveTwoBone(
			Vector3.Zero,
			Vector3.Forward,
			Vector3.Forward * 2,
			new Vector3( 1.5f, 0.4f, 0 ),
			Vector3.Up );
		Check( report, reachable.Reachable, "An in-range hand target must be reachable." );
		Near( report, new Vector3( 1.5f, 0.4f, 0 ), reachable.End, 0.001f, "Reachable target must be solved exactly." );

		var clamped = WeaponAnimationMath.SolveTwoBone(
			Vector3.Zero,
			Vector3.Forward,
			Vector3.Forward * 2,
			Vector3.Forward * 10,
			Vector3.Up );
		Check( report, !clamped.Reachable, "An overextended target must be reported." );
		Check( report, clamped.SolvedDistance < 2, "Overextension must clamp below total arm length." );
	}

	private static void TestConstraintDrivenIk( WeaponAnimatorSelfTestReport report )
	{
		var document = WeaponAnimationDocument.CreateDefault();
		document.Binding.Configuration = GripConfiguration.OneHanded;
		document.Binding.PrimaryHand.IsBound = true;
		document.Binding.PrimaryHand.Transform = new Transform( new Vector3( 1.5f, 0, 0 ) );
		document.Binding.PrimaryElbowPole.Transform = new Transform( new Vector3( 0, 0, 1 ) );

		var skeleton = new HostSkeleton();
		skeleton.Add( Bone( "root", "", Vector3.Zero ) );
		skeleton.Add( Bone( "arm_upper_R", "root", Vector3.Zero ) );
		skeleton.Add( Bone( "arm_lower_R", "arm_upper_R", new Vector3( 1, 0, 0 ) ) );
		skeleton.Add( Bone( "hand_R", "arm_lower_R", new Vector3( 2, 0, 0 ) ) );
		skeleton.Add( Bone( "bolt", "root", new Vector3( 1.2f, 0.8f, 0 ) ) );

		var clip = document.EnsureClip( WeaponClipRole.Idle );
		clip.Constraints.Add( new TimedConstraint
		{
			SourceControl = "@primary_hand",
			TargetBone = "bolt",
			StartTime = 0,
			EndTime = 1,
			MaintainOffset = false
		} );
		var pose = AnimationPoseEvaluator.Evaluate( document, skeleton, clip, 0.5f );
		Near( report, new Vector3( 1.2f, 0.8f, 0 ), pose.Model["hand_R"].Position, 0.002f, "Constraint must drive the IK target before the arm solve." );
	}

	private static void TestIkDescendantPropagation( WeaponAnimatorSelfTestReport report )
	{
		var document = WeaponAnimationDocument.CreateDefault();
		document.Binding.Configuration = GripConfiguration.OneHanded;
		document.Binding.PrimaryHand.IsBound = true;
		document.Binding.PrimaryHand.Transform = new Transform( new Vector3( 1.2f, 1.2f, 0 ) );
		document.Binding.PrimaryElbowPole.Transform = new Transform( new Vector3( 0, 0, 1 ) );

		var skeleton = new HostSkeleton();
		skeleton.Add( Bone( "root", "", Vector3.Zero ) );
		skeleton.Add( Bone( "arm_upper_R", "root", Vector3.Zero ) );
		skeleton.Add( Bone( "arm_lower_R", "arm_upper_R", new Vector3( 1, 0, 0 ) ) );
		skeleton.Add( Bone( "hand_R", "arm_lower_R", new Vector3( 2, 0, 0 ) ) );
		skeleton.Add( Bone( "finger_R", "hand_R", new Vector3( 2.5f, 0.2f, 0 ) ) );
		skeleton.Add( Bone( "forearm_twist_R", "arm_lower_R", new Vector3( 1.5f, 0, 0 ) ) );

		var pose = AnimationPoseEvaluator.Evaluate( document, skeleton, null, 0 );
		var fingerLocal = skeleton.GetBindLocal( skeleton.ByName["finger_R"] );
		var twistLocal = skeleton.GetBindLocal( skeleton.ByName["forearm_twist_R"] );
		Near(
			report,
			pose.Model["hand_R"].PointToWorld( fingerLocal.Position ),
			pose.Model["finger_R"].Position,
			0.001f,
			"Finger descendants must follow the solved hand." );
		Near(
			report,
			pose.Model["arm_lower_R"].PointToWorld( twistLocal.Position ),
			pose.Model["forearm_twist_R"].Position,
			0.001f,
			"Twist descendants must follow the solved forearm." );
	}

	private static void TestConstraintMaintainedOffset( WeaponAnimatorSelfTestReport report )
	{
		var document = WeaponAnimationDocument.CreateDefault();
		document.Binding.Configuration = GripConfiguration.OneHanded;
		document.Binding.PrimaryHand.IsBound = true;
		document.Binding.PrimaryHand.Transform = new Transform( new Vector3( 1.5f, 0, 0 ) );
		document.Binding.PrimaryElbowPole.Transform = new Transform( new Vector3( 0, 0, 1 ) );

		var skeleton = new HostSkeleton();
		skeleton.Add( Bone( "root", "", Vector3.Zero ) );
		skeleton.Add( Bone( "arm_upper_R", "root", Vector3.Zero ) );
		skeleton.Add( Bone( "arm_lower_R", "arm_upper_R", new Vector3( 1, 0, 0 ) ) );
		skeleton.Add( Bone( "hand_R", "arm_lower_R", new Vector3( 2, 0, 0 ) ) );
		skeleton.Add( Bone( "bolt", "root", new Vector3( 1, 0, 0 ) ) );

		var clip = document.EnsureClip( WeaponClipRole.Idle );
		var boltTrack = clip.EnsureTrack( "bolt" );
		WeaponAnimationMath.UpsertKey( boltTrack, 0, new Transform( new Vector3( 1, 0, 0 ) ) );
		WeaponAnimationMath.UpsertKey( boltTrack, 1, new Transform( new Vector3( 1.2f, 0, 0 ) ) );
		clip.Constraints.Add( new TimedConstraint
		{
			SourceControl = "@primary_hand",
			TargetBone = "bolt",
			StartTime = 0,
			EndTime = 1,
			MaintainOffset = true
		} );

		var pose = AnimationPoseEvaluator.Evaluate( document, skeleton, clip, 1 );
		Near( report, new Vector3( 1.7f, 0, 0 ), pose.Model["hand_R"].Position, 0.002f, "Maintain-offset constraints must preserve the start-frame hand offset." );
	}

	private static void TestControllerHistoryAndClipboard( WeaponAnimatorSelfTestReport report )
	{
		var controller = new WeaponAnimatorController();
		controller.SetDocument( WeaponAnimationDocument.CreateDefault( "History" ) );
		controller.Mutate( "Rename", document => document.Name = "Changed" );
		Check( report, controller.IsDirty && controller.CanUndo, "A mutation must mark the document dirty and create undo history." );
		controller.Undo();
		Equal( report, "History", controller.Document.Name, "Undo must restore the previous snapshot." );
		controller.Redo();
		Equal( report, "Changed", controller.Document.Name, "Redo must restore the changed snapshot." );
		var documentEvents = 0;
		var poseEvents = 0;
		controller.DocumentChanged += () => documentEvents++;
		controller.PoseChanged += () => poseEvents++;
		controller.BeginContinuousEdit( "Scrub name" );
		controller.UpdateContinuousEdit( document => document.Name = "Scrub A" );
		controller.UpdateContinuousEdit( document => document.Name = "Scrub B" );
		Equal(
			report,
			0,
			documentEvents,
			"A live scrub must not broadcast full document rebuilds while dragging." );
		Equal(
			report,
			2,
			poseEvents,
			"A live scrub must publish lightweight pose previews." );
		controller.EndContinuousEdit();
		Equal(
			report,
			1,
			documentEvents,
			"Completing a scrub must publish one consolidated document change." );
		controller.Undo();
		Equal( report, "Changed", controller.Document.Name, "A continuous drag must collapse into one undo step." );
		controller.Redo();
		Equal( report, "Scrub B", controller.Document.Name, "Redo must restore the final continuous-drag value." );

		var clip = controller.Document.GetSelectedClip()!;
		var track = clip.EnsureTrack( "weapon_root" );
		var key = WeaponAnimationMath.UpsertKey( track, 0, new Transform( new Vector3( 1, 2, 3 ) ) );
		controller.SelectKeys( [key.Id], false );
		controller.CopySelectedKeys();
		controller.SetTimelineTime( 0.5f );
		controller.PasteKeys();
		clip = controller.Document.GetSelectedClip()!;
		Equal( report, 2, clip.EnsureTrack( "weapon_root" ).Keys.Count, "Pasting keys must duplicate the clipboard payload." );
		Near(
			report,
			0.5f,
			clip.EnsureTrack( "weapon_root" ).Keys.Max( x => x.Time ),
			0.0001f,
			"Pasted keys must be offset to the playhead." );
	}

	private static void TestValidation( WeaponAnimatorSelfTestReport report )
	{
		var document = ValidDocument();
		Check( report, WeaponAnimationValidator.ValidateCalibration( document ).IsValid, "A complete calibration should pass." );
		Check( report, WeaponAnimationValidator.ValidateForGeneration( document ).IsValid, "Idle-only generation should pass with action warnings." );
		Check(
			report,
			WeaponAnimationValidator.ValidateForGeneration( document ).Issues.Any( x =>
				x.Severity == ValidationSeverity.Warning && x.Code == "clip.fallback" ),
			"Missing action clips must remain warnings." );
		document.Calibration.Anchors.RemoveAll( anchor =>
			anchor.Kind is AnchorKind.RearBore or AnchorKind.FrontBore );
		Check(
			report,
			WeaponAnimationValidator.ValidateCalibration( document ).IsValid,
			"Auto-align markers must not block an already-oriented weapon." );

		document.Rig.Bones.Add( new WeaponBoneDefinition { Name = "hand_R" } );
			Check(
				report,
				!WeaponAnimationValidator.ValidateCalibration( document ).IsValid,
				"Facepunch-reserved weapon bone names must block calibration." );

			document.Rig.Bones.RemoveAt( document.Rig.Bones.Count - 1 );
			document.Rig.Bones[0].Name = "root";
			document.Rig.Bones[0].Classification = WeaponBoneClassification.WeaponRoot;
			document.Rig.RootBone = "root";
			Check(
				report,
				WeaponAnimationValidator.ValidateCalibration( document ).IsValid,
				"A classified source root may use a reserved name before wrapper normalization." );
	}

	private static void TestRebase( WeaponAnimatorSelfTestReport report )
	{
		var document = WeaponAnimationDocument.CreateDefault();
		document.Rig.RootBone = "weapon_root";
		var idle = document.EnsureClip( WeaponClipRole.Idle );
		var rootTrack = idle.EnsureTrack( "weapon_root" );
		WeaponAnimationMath.UpsertKey( rootTrack, 0, new Transform( new Vector3( 2, 0, 0 ) ) );
		var previous = new CalibrationSnapshot
		{
			PhysicalTransform = Transform.Zero,
			FramingTransform = Transform.Zero
		};
		document.Calibration.PhysicalTransform = new Transform( new Vector3( 10, 0, 0 ) );
		CalibrationRebaser.RebaseAnimationData( document, previous );
		Near( report, 12, rootTrack.Keys[0].Position.x, 0.001f, "Root keys must retain their placement-relative offset." );
	}

	private static void TestSmdOutput( WeaponAnimatorSelfTestReport report )
	{
		var skeleton = new HostSkeleton();
		skeleton.Add( Bone( "root", "", Vector3.Zero ) );
		skeleton.Add( Bone( "weapon_root", "root", Vector3.Forward ) );
		var document = WeaponAnimationDocument.CreateDefault();
		document.Binding.Configuration = GripConfiguration.OneHanded;
		var clip = document.EnsureClip( WeaponClipRole.Idle );
		clip.Duration = 1;
		clip.SampleRate = 30;
		clip.Readiness = ClipReadiness.Ready;

		var reference = SmdWriter.WriteReference( skeleton );
		Check( report, reference.Contains( "triangles\nmaterials/dev/gray_25.vmat" ), "Reference SMD must contain the compiler carrier triangle." );
		var animation = SmdWriter.WriteClip( document, skeleton, clip );
		Check( report, animation.Contains( "time 30" ), "A one-second 30 fps clip must export its final frame." );
			Check( report, !animation.Contains( "NaN", StringComparison.OrdinalIgnoreCase ), "SMD output must contain finite transforms." );
		}

	private static void TestDmxOutput( WeaponAnimatorSelfTestReport report )
	{
		var skeleton = new HostSkeleton();
		skeleton.Add( Bone( "root", "", Vector3.Zero ) );
		skeleton.Add( Bone( "weapon_root", "root", Vector3.Forward ) );
		var first = DmxWriter.WriteReference( skeleton );
		var second = DmxWriter.WriteReference( skeleton );

		Check( report, first.StartsWith( "<!-- dmx encoding keyvalues2 4 format model 22 -->" ), "Host reference must use ModelDoc's supported DMX model format." );
		Check( report, first.Contains( "\"name\" \"string\" \"weapon_root\"" ), "Host reference must include every skeleton bone." );
		Check(
			report,
			first.Contains( "\"element\" \"" + DmxJointIdForTest( 0 ) + "\"," ),
			"DMX element array entries must be comma-delimited." );
		var blendIndices = first[first.IndexOf( "\"blendindices$0\" \"int_array\"", StringComparison.Ordinal )..];
		Check(
			report,
			blendIndices.Contains( "\t\t\"1\",\n\t\t\"1\",\n\t\t\"1\"\n", StringComparison.Ordinal ),
			"The carrier mesh must reference every host bone so ModelDoc cannot cull the skeleton." );
		Check(
			report,
			first.Contains( "\t\t\t\t\"3\",\n\t\t\t\t\"4\",\n\t\t\t\t\"5\",\n\t\t\t\t\"-1\"\n", StringComparison.Ordinal ),
			"The carrier mesh must emit one triangle per host bone." );
		Check(
			report,
			first.Contains( "materials/tools/toolsinvisible.vmat", StringComparison.Ordinal ),
			"The bone-retention carrier must use an invisible material." );
		Equal( report, first, second, "DMX host references must be deterministic." );
		var wrapper = ModelDocWriter.WriteSourceWrapper( "weapon.fbx", "root" );
		Check(
			report,
			wrapper.Contains( "original_bone_name = \"root\"" )
				&& wrapper.Contains( "new_bone_name = \"weapon_root\"" ),
			"Source wrappers must normalize the selected weapon root." );
		var host = ModelDocWriter.WriteHost(
			"host_reference.dmx",
			[],
			"",
			skeleton.Bones.Select( bone => bone.Name ) );
		Check(
			report,
			host.Contains( "target_bone = \"weapon_root\"", StringComparison.Ordinal )
				&& host.Contains( "do_not_discard = true", StringComparison.Ordinal )
				&& host.Contains( "materials/tools/toolsinvisible.vmat", StringComparison.Ordinal ),
			"Host ModelDocs must explicitly preserve generated bones." );
	}

	private static void TestFilteredSourceWrapper( WeaponAnimatorSelfTestReport report )
	{
		var wrapper = ModelDocWriter.WriteSourceWrapper(
			"weapons/test/source.fbx",
			"Armature",
			["foreign_arm", "foreign_camera"] );
		Check( report, wrapper.Contains( "_class = \"RenameBone\"" ), "A tool-owned source wrapper must normalize the root without modifying the original source." );
		Check( report, wrapper.Contains( "_class = \"RemoveBoneAndChildren\"" ), "A filtered source wrapper must remove excluded branch roots." );
		Check( report, wrapper.Contains( "\"foreign_arm\"" ) && wrapper.Contains( "\"foreign_camera\"" ), "Every excluded branch root must be emitted deterministically." );
		var vmdl = $"{ModelDocWriter.Header}\n{{ rootNode = {{ _class = \"RootNode\" children = [ ] }} }}";
		var adapted = ModelDocWriter.WriteVmdlSourceAdapter( vmdl, "root", ["foreign_arm"] );
		Check(
			report,
			adapted.Contains( "_class = \"ModelModifierList\"" )
				&& adapted.Contains( "original_bone_name = \"root\"" )
				&& adapted.Contains( "\"foreign_arm\"" ),
			"VMDL inputs must receive the same tool-owned root normalization and branch filtering." );
	}

	private static string DmxJointIdForTest( int index )
	{
		var bytes = System.Security.Cryptography.SHA256.HashData(
			System.Text.Encoding.UTF8.GetBytes( $"SboxWeaponAnimator.DmxReference:joint:{index}" ) );
		return new Guid( bytes.AsSpan( 0, 16 ) ).ToString();
	}

	private static void TestDeterministicOutput( WeaponAnimatorSelfTestReport report )
	{
		var document = ValidDocument();
		var idle = document.EnsureClip( WeaponClipRole.Idle );
		var originalCulture = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo( "fr-FR" );
				var graphFrench = AnimGraphWriter.Write( document, "weapons/test/host.vmdl" );
				var modelFrench = ModelDocWriter.WriteHost(
					"host_reference.dmx",
					[(idle, "idle.smd")],
					"weapon.vanmgrph",
					["root", "weapon_root"] );
			var prefabFrench = PrefabWriter.Write( document, "host.vmdl", "weapon.vmdl" );

			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo( "en-US" );
			Equal( report, graphFrench, AnimGraphWriter.Write( document, "weapons/test/host.vmdl" ), "AnimGraph output must be culture-independent." );
			Equal(
					report,
					modelFrench,
					ModelDocWriter.WriteHost(
						"host_reference.dmx",
						[(idle, "idle.smd")],
						"weapon.vanmgrph",
						["root", "weapon_root"] ),
				"ModelDoc output must be culture-independent." );
			Equal( report, prefabFrench, PrefabWriter.Write( document, "host.vmdl", "weapon.vmdl" ), "Prefab output must be culture-independent." );
			Equal( report, AnimGraphWriter.Id( "node:Root" ), AnimGraphWriter.Id( "node:Root" ), "Deterministic graph IDs must be stable." );
		}
		finally
		{
			CultureInfo.CurrentCulture = originalCulture;
		}
	}

	private static void TestAnimGraphTagsAndFallbacks( WeaponAnimatorSelfTestReport report )
	{
		var document = ValidDocument();
		var idle = document.EnsureClip( WeaponClipRole.Idle );
		idle.Tags.Add( new AnimationTag
		{
			Name = "attack_discouraged",
			Kind = AnimationTagKind.Range,
			StartTime = 0.2f,
			EndTime = 0.6f
		} );
		var graph = AnimGraphWriter.Write( document, "host.vmdl" );
		Check( report, graph.Contains( "_class = \"CAnimTagSpan\"" ), "Authored tags must become sequence tag spans." );
		Check( report, graph.Contains( "m_fStartCycle = 0.2" ), "Tag start time must be normalized to sequence cycle." );
		Check(
			report,
			Count( graph, "m_sequenceName = \"idle\"" ) > 1,
			"Missing action clips must use Idle sequence fallbacks." );
		Check( report, graph.Contains( "m_name = \"b_attack\"" ), "Facepunch firearm parameters must be exposed." );
		Check( report, graph.Contains( "m_name = \"reload_increment\"" ), "Standard reload tags must be declared." );
	}

	private static WeaponAnimationDocument ValidDocument()
	{
		var document = WeaponAnimationDocument.CreateDefault( "Test Weapon" );
		document.Source.SourcePath = "weapons/test/source.fbx";
		document.Source.CompiledModelPath = "weapons/test/source.vmdl";
		document.Source.Compiled = true;
		document.Source.PreviewHostCompiled = true;
		document.Rig.RootBone = "weapon_root";
		document.Rig.Bones.Add( new WeaponBoneDefinition
		{
			Id = "weapon_root",
			HierarchyPath = "weapon_root",
			Name = "weapon_root",
			OriginalName = "weapon_root",
			Classification = WeaponBoneClassification.WeaponRoot,
			Inclusion = WeaponBoneInclusion.Included,
			BindTransform = Transform.Zero,
			BindModelTransform = Transform.Zero,
			BindLocalTransform = Transform.Zero,
			HasSkinInfluence = true
		} );
		document.Rig.SourceSkeletonRootId = "weapon_root";
		document.Rig.WeaponSubtreeRootId = "weapon_root";
		document.Rig.ReviewRequired = false;
		document.Rig.FilteredPreviewConfirmed = true;
		document.Calibration.SetAnchor( Anchor( AnchorKind.Grip, new Vector3( 1, 0, 0 ) ) );
		document.Calibration.SetAnchor( Anchor( AnchorKind.RearBore, Vector3.Zero ) );
		document.Calibration.SetAnchor( Anchor( AnchorKind.FrontBore, Vector3.Forward ) );
		document.Calibration.Confirmed = true;
		document.Calibration.Snapshot = new CalibrationSnapshot();
		document.EnsureClip( WeaponClipRole.Idle ).Readiness = ClipReadiness.Ready;
		return document;
	}

	private static WeaponAnchor Anchor( AnchorKind kind, Vector3 position ) => new()
	{
		Name = kind.ToString(),
		Kind = kind,
		BoneName = "weapon_root",
		LocalPosition = position
	};

	private static WeaponBoneDefinition Definition(
		string name,
		string parent,
		WeaponBoneClassification classification,
		Vector3 modelPosition ) =>
		Definition( name, parent, classification, new Transform( modelPosition ) );

	private static WeaponBoneDefinition Definition(
		string name,
		string parent,
		WeaponBoneClassification classification,
		Transform modelTransform ) => new()
	{
		Name = name,
		ParentName = parent,
		OriginalName = name,
		OriginalParentName = parent,
		Classification = classification,
		Inclusion = WeaponBoneInclusion.Included,
		BindTransform = modelTransform,
		BindModelTransform = modelTransform,
		HasSkinInfluence = true
	};

	private static HostBone Bone( string name, string parent, Vector3 position ) => new()
	{
		Name = name,
		ParentName = parent,
		BindModelTransform = new Transform( position )
	};

	private static float RotationLength( Rotation value ) =>
		MathF.Sqrt( value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w );

	private static int Count( string value, string fragment )
	{
		var count = 0;
		var offset = 0;
		while ( (offset = value.IndexOf( fragment, offset, StringComparison.Ordinal )) >= 0 )
		{
			count++;
			offset += fragment.Length;
		}
		return count;
	}

	private static void Run(
		WeaponAnimatorSelfTestReport report,
		string name,
		Action<WeaponAnimatorSelfTestReport> test )
	{
		try
		{
			test( report );
		}
		catch ( Exception ex )
		{
			report.Failures.Add( $"{name}: threw {ex.GetType().Name}: {ex.Message}" );
		}
	}

	private static void Check(
		WeaponAnimatorSelfTestReport report,
		bool condition,
		string message )
	{
		if ( condition )
			report.Passed++;
		else
			report.Failures.Add( message );
	}

	private static void Equal<T>(
		WeaponAnimatorSelfTestReport report,
		T expected,
		T actual,
		string message )
	{
		Check(
			report,
			EqualityComparer<T>.Default.Equals( expected, actual ),
			$"{message} Expected '{expected}', got '{actual}'." );
	}

	private static void Near(
		WeaponAnimatorSelfTestReport report,
		float expected,
		float actual,
		float tolerance,
		string message )
	{
		Check(
			report,
			MathF.Abs( expected - actual ) <= tolerance,
			$"{message} Expected {expected}, got {actual}." );
	}

	private static void Near(
		WeaponAnimatorSelfTestReport report,
		Vector3 expected,
		Vector3 actual,
		float tolerance,
		string message )
	{
		Check(
			report,
			expected.Distance( actual ) <= tolerance,
			$"{message} Expected {expected}, got {actual}." );
	}
}
