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
		Run( report, "stepped part visibility", TestPartVisibility );
		Run( report, "schema migration", TestSchemaMigration );
		Run( report, "content-sized buttons", TestContentSizedButtons );
		Run( report, "alignment", TestAlignment );
		Run( report, "track interpolation", TestInterpolation );
		Run( report, "curve editor v2", TestCurveEditorV2 );
		Run( report, "frame snapping", TestFrameSnapping );
		Run( report, "timeline navigation", TestTimelineNavigation );
		Run( report, "timeline selection and movement", TestTimelineSelectionAndMovement );
		Run( report, "timeline key reversal", TestTimelineKeyReversal );
		Run( report, "timeline playback", TestTimelinePlayback );
		Run( report, "two-bone IK", TestTwoBoneIk );
		Run( report, "IK descendant propagation", TestIkDescendantPropagation );
		Run( report, "timed constraints before IK", TestConstraintDrivenIk );
		Run( report, "constraint maintained offset", TestConstraintMaintainedOffset );
		Run( report, "history and key clipboard", TestControllerHistoryAndClipboard );
		Run( report, "calibration and generation validation", TestValidation );
		Run( report, "generation output paths", TestGenerationOutputPaths );
		Run( report, "material discovery and output", TestMaterialPipeline );
		Run( report, "generated file removal", TestGeneratedFileRemoval );
		Run( report, "calibration rebase", TestRebase );
		Run( report, "DMX output", TestDmxOutput );
		Run( report, "filtered source wrapper", TestFilteredSourceWrapper );
		Run( report, "generation source adapters", TestGenerationSourceAdapters );
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
		Check(
			report,
			document.Workspace.RimLightEnabled,
			"The cyan viewport edge light must remain available by default." );
		Near(
			report,
			4.0f,
			document.Workspace.RimLightIntensity,
			0.0001f,
			"The edge light default must be restrained rather than the old over-bright value." );
		Near(
			report,
			1.0f,
			document.Workspace.CameraMoveSpeed,
			0.0001f,
			"The free-look camera must start at normal movement speed." );
		Check(
			report,
			document.Workspace.SnapRotation,
			"Rotation snapping must be enabled in new projects." );
		Near(
			report,
			15.0f,
			document.Workspace.RotationSnapDegrees,
			0.0001f,
			"Rotation snapping must start at the familiar 15-degree step." );
		Near(
			report,
			30.0f,
			WeaponAnimatorViewport.AdjustRotationSnapAngle( 15.0f, 1 ),
			0.0001f,
			"The snap-angle stepper must advance through the standard angle presets." );
		Near(
			report,
			5.0f,
			WeaponAnimatorViewport.AdjustRotationSnapAngle( 15.0f, -1 ),
			0.0001f,
			"The snap-angle stepper must move backward through the standard angle presets." );
		Near(
			report,
			0.25f,
			WeaponAnimatorViewport.AdjustRotationSnapAngle( 0.25f, -1 ),
			0.0001f,
			"The snap-angle stepper must retain its lower bound." );
		Near(
			report,
			180.0f,
			WeaponAnimatorViewport.AdjustRotationSnapAngle( 180.0f, 1 ),
			0.0001f,
			"The snap-angle stepper must retain its upper bound." );
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
		var rimStyle = ViewportRimLightStyle.Resolve(
			document.Workspace.RimLightEnabled,
			document.Workspace.RimLightIntensity,
			false );
		Check(
			report,
			rimStyle.Enabled,
			"The edge-light preference must enable the cyan point light in lit mode." );
		Near(
			report,
			4.0f,
			rimStyle.Intensity,
			0.0001f,
			"The viewport must apply the persisted edge-light brightness." );
		Check(
			report,
			!ViewportRimLightStyle.Resolve( true, 4, true ).Enabled
				&& !ViewportRimLightStyle.Resolve( false, 4, false ).Enabled,
			"Full Bright and the explicit toggle must both disable the edge light." );
		Near(
			report,
			12,
			ViewportRimLightStyle.Resolve( true, 99, false ).Intensity,
			0.0001f,
			"Edge-light brightness must remain inside its supported range." );
		var fullBrightArms = ArmPreviewVisualStyle.Resolve(
			WeaponAnimatorStage.Animate,
			true );
		Check(
			report,
			fullBrightArms.UseFlatMaterial
				&& MathF.Max(
					fullBrightArms.Tint.r,
					MathF.Max( fullBrightArms.Tint.g, fullBrightArms.Tint.b ) ) > 0.1f,
			"Full Bright must use a visible neutral arms material instead of rendering skin black." );
		Check(
			report,
			!ArmPreviewVisualStyle.Resolve( WeaponAnimatorStage.Animate, false ).UseFlatMaterial,
			"Lit animation preview must preserve the production arms materials." );

		var first = WeaponAnimationClip.Create( WeaponClipRole.Custom );
		var second = WeaponAnimationClip.Create( WeaponClipRole.Custom );
		first.Name = second.Name = "Mechanical Check";
		Check(
			report,
			WeaponAnimationNames.SequenceName( first ) != WeaponAnimationNames.SequenceName( second ),
			"Custom sequence names must remain unique." );
		document.Clips.Add( first );
		document.Clips.Add( second );
		Check(
			report,
			WeaponAnimationNames.RepairCustomSequenceNames( document )
				&& !first.GeneratedSequenceName.Contains( first.Id.ToString( "N" ), StringComparison.Ordinal )
				&& !second.GeneratedSequenceName.Contains( second.Id.ToString( "N" ), StringComparison.Ordinal )
				&& first.GeneratedSequenceName != second.GeneratedSequenceName,
			"Custom clips must receive stable, readable sequence names with short collision suffixes." );
		var customSequence = first.GeneratedSequenceName;
		Check(
			report,
			!WeaponAnimationNames.RepairCustomSequenceNames( document )
				&& first.GeneratedSequenceName == customSequence,
			"Resolved custom sequence names must remain stable across later repairs." );
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
		var auditSignature = RigAuditPanel.BoneStructureSignature( rig, "", true, true, false );
		rig.ReviewRequired = true;
		Equal(
			report,
			auditSignature,
			RigAuditPanel.BoneStructureSignature( rig, "", true, true, false ),
			"Non-structural document refreshes must not rebuild the rig-audit bone rows." );
		rig.FindBone( "receiver" )!.Classification = WeaponBoneClassification.Structural;
		Check(
			report,
			auditSignature != RigAuditPanel.BoneStructureSignature( rig, "", true, true, false ),
			"Classification changes must rebuild the rig-audit bone rows." );
		rig.FindBone( "receiver" )!.Classification = WeaponBoneClassification.Animatable;

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

		var rebuiltHierarchy = new HostSkeleton();
		rebuiltHierarchy.Add( new HostBone
		{
			Name = "root",
			BindModelTransform = new Transform( new Vector3( 4, 0, 0 ) ),
			BindLocalTransform = new Transform( new Vector3( 4, 0, 0 ) ),
			HasExplicitBindLocal = true
		} );
		rebuiltHierarchy.Add( new HostBone
		{
			Name = "weapon_root",
			ParentName = "root",
			BindModelTransform = new Transform( new Vector3( 999 ) ),
			BindLocalTransform = new Transform(
				new Vector3( 2, 0, 0 ),
				Rotation.FromYaw( 90 ),
				0.5f ),
			HasExplicitBindLocal = true
		} );
		rebuiltHierarchy.Add( new HostBone
		{
			Name = "weapon_helper",
			ParentName = "weapon_root",
			BindModelTransform = new Transform( new Vector3( -999 ) ),
			BindLocalTransform = new Transform( new Vector3( 2, 0, 0 ) ),
			HasExplicitBindLocal = true
		} );
		rebuiltHierarchy.RebuildModelTransformsFromLocals();
		var expectedHelper = WeaponAnimationMath.Compose(
			rebuiltHierarchy.ByName["weapon_root"].BindModelTransform,
			rebuiltHierarchy.ByName["weapon_helper"].BindLocalTransform );
		Near(
			report,
			expectedHelper.Position,
			rebuiltHierarchy.ByName["weapon_helper"].BindModelTransform.Position,
			0.0001f,
			"Changing weapon_root must rebuild canonical helper model transforms from their untouched local binds." );
		Near(
			report,
			expectedHelper.Scale,
			rebuiltHierarchy.ByName["weapon_helper"].BindModelTransform.Scale,
			0.0001f,
			"Rebuilt helper binds must preserve the calibrated parent scale exactly once." );
		var compilerBinds = rebuiltHierarchy.BuildCompilerBindModelTransforms();
		var compilerRoot = compilerBinds["weapon_root"];
		var compilerHelper = compilerBinds["weapon_helper"];
		Near(
			report,
			Vector3.One,
			compilerRoot.Scale,
			0.0001f,
			"Compiled bind expectations must model ModelDoc's scale-one skeleton." );
		Near(
			report,
			rebuiltHierarchy.ByName["weapon_helper"].BindModelTransform.Position,
			compilerHelper.Position,
			0.0001f,
			"Compiled bind expectations must preserve scale-baked physical child pivots." );
		Equal(
			report,
			"weapon_helper",
			rebuiltHierarchy.ChildrenOf( "weapon_root" ).Single().Name,
			"Host skeletons must retain a direct parent-to-children lookup." );

		var cachedA = HostSkeletonBuilder.BuildCached( document, includeArmProfile: false );
		var cachedB = HostSkeletonBuilder.BuildCached( document, includeArmProfile: false );
		Check(
			report,
			ReferenceEquals( cachedA, cachedB ),
			"Unchanged rig inputs must reuse the cached animation-host skeleton." );
		document.Calibration.PhysicalTransform =
			document.Calibration.PhysicalTransform.WithPosition( new Vector3( 99, 0, 0 ) );
		var cachedChanged = HostSkeletonBuilder.BuildCached( document, includeArmProfile: false );
		Check(
			report,
			!ReferenceEquals( cachedA, cachedChanged ),
			"Calibration changes must invalidate the cached animation-host skeleton." );
		document.Calibration.PhysicalTransform =
			document.Calibration.PhysicalTransform.WithPosition( new Vector3( 99.00001f, 0, 0 ) );
		Check(
			report,
			!ReferenceEquals(
				cachedChanged,
				HostSkeletonBuilder.BuildCached( document, includeArmProfile: false ) ),
			"Sub-display-precision transform changes must invalidate the host cache." );
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
		skeleton.Add( Bone( "arm_upper_L", "root", Vector3.Zero ) );
		Equal(
			report,
			1,
			skeleton.ByName["arm_lower_R"].ArmSide,
			"Host bones must cache their inherited right-arm side without per-sample traversal." );
		Equal(
			report,
			-1,
			skeleton.ByName["arm_upper_L"].ArmSide,
			"Host bones must cache their left-arm side when added." );

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
		var leftTrack = idle.EnsureTrack( "arm_upper_L" );
		leftTrack.Kind = RigControlKind.Arm;
		Check(
			report,
			!AnimationPoseEvaluator.ShouldEvaluateTrack( document, skeleton, leftTrack ),
			"One-handed primary binding must not enable left-arm tracks." );
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

		var exportWithWorkingPose = DmxWriter.WriteAnimation( document, skeleton, clip );
		document.Workspace.WorkingPoseOverrides.Clear();
		var exportWithoutWorkingPose = DmxWriter.WriteAnimation( document, skeleton, clip );
		Equal(
			report,
			exportWithoutWorkingPose,
			exportWithWorkingPose,
			"Working poses must not affect deterministic animation output." );

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

		var beforeCalibration = controller.Document.Calibration.PhysicalTransform;
		controller.BeginContinuousEdit( "Move calibrated weapon" );
		controller.UpdateContinuousEdit( current =>
			current.Calibration.PhysicalTransform =
				beforeCalibration.WithPosition( new Vector3( 1, 2, 3 ) ) );
		controller.UpdateContinuousEdit( current =>
			current.Calibration.PhysicalTransform =
				beforeCalibration.WithPosition( new Vector3( 4, 5, 6 ) ) );
		controller.EndContinuousEdit();
		controller.Undo();
		Near(
			report,
			beforeCalibration.Position,
			controller.Document.Calibration.PhysicalTransform.Position,
			0.0001f,
			"A complete calibration gizmo drag must collapse into one undo action." );

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

		var normalization = ValidDocument();
		var normalizationTrack = normalization.EnsureClip( WeaponClipRole.Idle )
			.EnsureTrack( "weapon_root" );
		normalizationTrack.Keys =
		[
			new TransformKey { Time = 1 },
			new TransformKey { Time = 0 }
		];
		var custom = WeaponAnimationClip.Create( WeaponClipRole.Custom );
		custom.Name = "Check Action";
		normalization.Clips.Add( custom );
		var normalized = WeaponAnimationMigration.MigrateAndRepair( normalization );
		Check(
			report,
			normalized.RepairedKeyOrder
				&& normalizationTrack.Keys[0].Time == 0
				&& normalizationTrack.Keys[1].Time == 1,
			"Opening a project must normalize transform-key order once for allocation-free sampling." );
		Check(
			report,
			normalized.RepairedSequenceNames
				&& custom.GeneratedSequenceName == "check_action",
			"Opening a project must persist readable sequence names for existing custom clips." );

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
		var idleClip = document.EnsureClip( WeaponClipRole.Idle );
		var deployClip = document.EnsureClip( WeaponClipRole.Deploy );
		var idleButton = clips.GetClipButton( idleClip.Id );
		var deployButton = clips.GetClipButton( deployClip.Id );
		clips.ClipScroll.VerticalScrollbar.Maximum = 500;
		clips.ClipScroll.VerticalScrollbar.Value = 118;
		clips.PropertiesScroll!.VerticalScrollbar.Maximum = 500;
		clips.PropertiesScroll.VerticalScrollbar.Value = 37;
		controller.SelectClip( deployClip.Id );
		Equal(
			report,
			118,
			clips.ClipScroll.VerticalScrollbar.Value,
			"Changing clips must preserve the clip-rack scroll position." );
		Equal(
			report,
			0,
			clips.PropertiesScroll.VerticalScrollbar.Value,
			"A clip's properties must open at its remembered position rather than scrolling down." );
		Check(
			report,
			ReferenceEquals( idleButton, clips.GetClipButton( idleClip.Id ) )
				&& ReferenceEquals( deployButton, clips.GetClipButton( deployClip.Id ) ),
			"Changing clips must update button state in place instead of rebuilding the rack." );
		clips.PropertiesScroll.VerticalScrollbar.Maximum = 500;
		clips.PropertiesScroll.VerticalScrollbar.Value = 19;
		controller.SelectClip( idleClip.Id );
		Equal(
			report,
			37,
			clips.PropertiesScroll.VerticalScrollbar.Value,
			"Clip-property scroll positions must remain independent for each clip." );
		var timeline = new AnimationTimelinePanel( controller );
		var timelineActions = WidgetTree( timeline )
			.OfType<WeaponAnimatorButton>()
			.Where( x => x.Text is "Add key" or "Copy" or "Paste" or "Reverse" or "Curves" )
			.ToArray();
		Equal(
			report,
			5,
			timelineActions.Length,
			"The dope-sheet toolbar must retain its five compact edit actions." );
		Check(
			report,
			WidgetTree( timeline )
				.OfType<WeaponAnimatorButton>()
				.All( x => x.Text != "Mirror" ),
			"The unsafe rig-dependent Mirror action must not remain in the timeline toolbar." );
		Check(
			report,
			timelineActions.All( x =>
				x.MinimumWidth >= x.PreferredWidth
				&& x.MinimumWidth <= MathF.Ceiling( x.PreferredWidth ) + 0.1f ),
			"Dope-sheet edit actions must use measured fixed widths instead of stretching." );
		Near(
			report,
			500,
			TimelineControlToolbar.CenteredLeft( 1000, 150 ) + 75,
			0.0001f,
			"Timeline transport controls must be centered independently of unequal side content." );
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

	private static IEnumerable<Widget> WidgetTree( Widget widget )
	{
		yield return widget;
		foreach ( var child in widget.Children )
		{
			foreach ( var descendant in WidgetTree( child ) )
				yield return descendant;
		}
	}

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

	private static void TestCurveEditorV2( WeaponAnimatorSelfTestReport report )
	{
		var document = WeaponAnimationDocument.CreateDefault( "Curves" );
		var clip = document.GetSelectedClip()!;
		clip.Duration = 2;
		clip.SampleRate = 30;
		clip.Tracks.Clear();
		foreach ( var (target, kind) in new[]
		{
			("weapon_root", RigControlKind.Weapon),
			("finger_index_1_R", RigControlKind.Arm),
			("@primary_hand", RigControlKind.Arm),
			("camera", RigControlKind.Camera)
		} )
		{
			var keyed = clip.EnsureTrack( target );
			keyed.Kind = kind;
			WeaponAnimationMath.UpsertKey( keyed, 0, Transform.Zero );
		}
		Equal(
			report,
			4,
			CurveEditingService.KeyedTracks( clip ).Count,
			"Curve track enumeration must include every keyed weapon, arm, target, and camera track." );
		Equal(
			report,
			1,
			CurveEditingService.KeyedTracks( clip, "finger" ).Count,
			"Curve track search must filter without truncating the keyed-track source." );

		var controller = new WeaponAnimatorController();
		controller.SetDocument( document );
		controller.SetCurveEditorVisible( true );
		Check(
			report,
			document.Workspace.CurveEditorVisible,
			"The Curves toggle must enter persistent curve-editor mode." );
		controller.SelectCurveTrack( clip, clip.Tracks[^1].Id );
		controller.SetCurveMode( clip, CurveEditorMode.Channels );
		controller.SetCurveChannels(
			clip,
			TransformCurveChannel.PositionX | TransformCurveChannel.RotationY );
		var view = document.Workspace.EnsureCurveView( clip.Id );
		Equal(
			report,
			clip.Tracks[^1].Id,
			view.SelectedTrackId,
			"Selected curve tracks must persist per clip." );
		Check(
			report,
			(view.VisibleChannels & TransformCurveChannel.RotationY) != 0,
			"Multiple visible transform channels must persist together." );

		var motion = new TransformTrack { Interpolation = TrackInterpolation.Cubic };
		var start = WeaponAnimationMath.UpsertKey(
			motion,
			0,
			new Transform( Vector3.Zero, Rotation.FromYaw( 170 ), Vector3.One ) );
		var end = WeaponAnimationMath.UpsertKey(
			motion,
			1,
			new Transform(
				new Vector3( 10, 0, 0 ),
				Rotation.FromYaw( -170 ),
				new Vector3( 1, 3, 1 ) ) );
		CurveEditingService.ApplyPreset(
			motion,
			[],
			CurveEditorMode.Speed,
			TransformCurveChannel.PositionX,
			CurvePreset.EaseIn );
		var speedSpan = motion.FindCurveSpan( start.Id, end.Id )!;
		Near(
			report,
			1,
			WeaponAnimationMath.MotionRateArea( speedSpan.Speed ),
			0.001f,
			"Ease-in speed curves must normalize to a complete one-span traversal." );
		Near(
			report,
			0,
			WeaponAnimationMath.SampleMotionRate( speedSpan.Speed, 0 ),
			0.0001f,
			"Ease-in speed must begin at 0×." );
		Near(
			report,
			2,
			WeaponAnimationMath.SampleMotionRate( speedSpan.Speed, 1 ),
			0.0001f,
			"Ease-in speed must end at 2×." );
		Near(
			report,
			2.5f,
			WeaponAnimationMath.SampleTrack( motion, 0.5f, Transform.Zero ).Position.x,
			0.02f,
			"Integrated speed must drive monotonic normalized motion progress." );

		speedSpan.Speed = new MotionRateCurve
		{
			StartRate = -2,
			EndRate = -1
		};
		Near(
			report,
			0,
			WeaponAnimationMath.SampleMotionRate( speedSpan.Speed, 0.5f ),
			0.0001f,
			"Motion-rate curves must clamp negative rates at 0×." );
		Near(
			report,
			0.5f,
			WeaponAnimationMath.SampleMotionProgress( speedSpan.Speed, 0.5f ),
			0.0001f,
			"Zero-area speed curves must fall back to linear timing." );

		speedSpan.HasSpeedCurve = false;
		speedSpan.HasInterpolationOverride = true;
		speedSpan.Interpolation = TrackInterpolation.Linear;
		CurveEditingService.ApplyPreset(
			motion,
			[],
			CurveEditorMode.Channels,
			TransformCurveChannel.PositionX
				| TransformCurveChannel.RotationY
				| TransformCurveChannel.ScaleY,
			CurvePreset.EaseInOut );
		var quarter = WeaponAnimationMath.SampleTrack( motion, 0.25f, Transform.Zero );
		Near(
			report,
			1.5625f,
			quarter.Position.x,
			0.01f,
			"Position channel tangents must evaluate as cubic Hermite curves." );
		Near(
			report,
			1.3125f,
			quarter.Scale.y,
			0.01f,
			"Scale channel tangents must evaluate independently." );
		var rotationSample = WeaponAnimationMath.SampleTrack( motion, 0.5f, Transform.Zero );
		Near(
			report,
			1,
			RotationLength( rotationSample.Rotation ),
			0.0001f,
			"Custom Euler rotation channels must normalize their output quaternion." );
		Check(
			report,
			MathF.Abs( MathF.Abs( rotationSample.Rotation.Angles().yaw ) - 180 ) < 1,
			"Rotation channels must unwrap through the shortest angular path." );
		Check(
			report,
			(start.CurveTangents.FreeHandles & TransformCurveChannel.PositionX) == 0,
			"Curve handles must be aligned by default." );
		CurveEditingService.SetTangent(
			start,
			TransformCurveChannel.PositionX,
			false,
			4,
			true );
		Check(
			report,
			(start.CurveTangents.FreeHandles & TransformCurveChannel.PositionX) != 0,
			"Alt-style tangent edits must be able to break one handle side." );
		CurveEditingService.AlignHandles(
			start,
			TransformCurveChannel.PositionX );
		Near(
			report,
			CurveEditingService.GetTangent(
				start,
				TransformCurveChannel.PositionX,
				true ),
			CurveEditingService.GetTangent(
				start,
				TransformCurveChannel.PositionX,
				false ),
			0.0001f,
			"Handle alignment must restore matching facing tangents." );

		var topology = new TransformTrack();
		var first = WeaponAnimationMath.UpsertKey(
			topology, 0, new Transform( Vector3.Zero ) );
		var middle = WeaponAnimationMath.UpsertKey(
			topology, 1, new Transform( Vector3.One ) );
		var last = WeaponAnimationMath.UpsertKey(
			topology, 2, new Transform( Vector3.One * 2 ) );
		topology.EnsureCurveSpan( first.Id, middle.Id ).HasSpeedCurve = true;
		topology.EnsureCurveSpan( middle.Id, last.Id ).HasSpeedCurve = true;
		CurveEditingService.RemoveKeysAndRepair( topology, x => x.Id == middle.Id );
		var repaired = topology.FindCurveSpan( first.Id, last.Id );
		Check(
			report,
			repaired?.HasInterpolationOverride == true
				&& repaired.Interpolation == TrackInterpolation.Linear,
			"Deleting a curve endpoint must create a safe linear bridge between new neighbors." );

		var legacy = WeaponAnimationDocument.CreateDefault( "Schema 3 curves" );
		legacy.SchemaVersion = 3;
		var legacyClip = legacy.EnsureClip( WeaponClipRole.Fire );
		var legacyTrack = legacyClip.EnsureTrack( "legacy" );
		legacyTrack.Interpolation = TrackInterpolation.Cubic;
		WeaponAnimationMath.UpsertKey(
			legacyTrack, 0, new Transform( Vector3.Zero ) );
		WeaponAnimationMath.UpsertKey(
			legacyTrack, 1, new Transform( new Vector3( 10, 0, 0 ) ) );
		var before = WeaponAnimationMath.SampleTrack(
			legacyTrack, 0.25f, Transform.Zero );
		var migration = WeaponAnimationMigration.MigrateAndRepair( legacy );
		var after = WeaponAnimationMath.SampleTrack(
			legacyTrack, 0.25f, Transform.Zero );
		Check(
			report,
			migration.CurveSchemaMigrated
				&& legacy.SchemaVersion == WeaponAnimationDocument.CurrentSchemaVersion,
			"Schema-v3 documents must migrate to schema v4." );
		Near(
			report,
			before.Position,
			after.Position,
			0.0001f,
			"Schema-v3 migration must preserve exact legacy playback." );
		Check(
			report,
			legacyTrack.CurveSpans.Count == 0,
			"Migration must not materialize custom curve spans until edited." );

		var lifecycleDocument = WeaponAnimationDocument.CreateDefault( "Curve lifecycle" );
		var lifecycleClip = lifecycleDocument.GetSelectedClip()!;
		lifecycleClip.Duration = 2;
		lifecycleClip.SampleRate = 30;
		lifecycleClip.Tracks.Clear();
		var lifecycleTrack = lifecycleClip.EnsureTrack( "slide" );
		var lifecycleStart = WeaponAnimationMath.UpsertKey(
			lifecycleTrack, 0, new Transform( Vector3.Zero ) );
		var lifecycleEnd = WeaponAnimationMath.UpsertKey(
			lifecycleTrack, 1, new Transform( new Vector3( 4, 0, 0 ) ) );
		CurveEditingService.ApplyPreset(
			lifecycleTrack,
			[],
			CurveEditorMode.Speed,
			TransformCurveChannel.PositionX,
			CurvePreset.EaseOut );
		var lifecycleSpanId = lifecycleTrack.CurveSpans.Single().Id;
		var lifecycleController = new WeaponAnimatorController();
		lifecycleController.SetDocument( lifecycleDocument );
		lifecycleController.SetSelectedKeys(
			[lifecycleStart.Id, lifecycleEnd.Id] );
		var starts = lifecycleTrack.Keys.ToDictionary( x => x.Id, x => x.Time );
		lifecycleController.BeginSelectedKeyMove();
		lifecycleController.UpdateSelectedKeyMove( starts, 5 );
		lifecycleController.EndSelectedKeyMove( starts, 5 );
		lifecycleTrack = lifecycleController.Document.GetSelectedClip()!
			.Tracks.Single( x => x.Target == "slide" );
		Check(
			report,
			lifecycleTrack.CurveSpans.Any( x => x.Id == lifecycleSpanId ),
			"Moving curve endpoints must retain their stable span data." );

		lifecycleController.CopySelectedKeys();
		lifecycleController.SetTimelineFrame( 5 );
		lifecycleController.PasteKeys();
		lifecycleTrack = lifecycleController.Document.GetSelectedClip()!
			.Tracks.Single( x => x.Target == "slide" );
		Check(
			report,
			lifecycleTrack.CurveSpans.Any( x =>
				x.HasSpeedCurve
					&& lifecycleController.SelectedKeys.Contains( x.StartKeyId )
					&& lifecycleController.SelectedKeys.Contains( x.EndKeyId ) ),
			"Copy and paste must preserve a span curve only when both endpoint keys are copied." );

		var invalidDocument = ValidDocument();
		var invalidClip = invalidDocument.EnsureClip( WeaponClipRole.Fire );
		var invalidTrack = invalidClip.EnsureTrack( "weapon_root" );
		var invalidStart = WeaponAnimationMath.UpsertKey(
			invalidTrack, 0, new Transform( Vector3.Zero ) );
		var invalidEnd = WeaponAnimationMath.UpsertKey(
			invalidTrack, 1, new Transform( Vector3.One ) );
		var invalidSpan = invalidTrack.EnsureCurveSpan(
			invalidStart.Id, invalidEnd.Id );
		invalidSpan.HasSpeedCurve = true;
		invalidSpan.Speed = new MotionRateCurve
		{
			StartRate = -1,
			EndRate = -1
		};
		Check(
			report,
			WeaponAnimationValidator.ValidateForGeneration( invalidDocument )
				.Issues.Any( x => x.Code == "curve.speed_invalid" ),
			"Zero-area speed curves must produce an explicit validation warning." );

		var exportDocument = WeaponAnimationDocument.CreateDefault( "Curve export" );
		var exportClip = exportDocument.GetSelectedClip()!;
		exportClip.Duration = 1;
		exportClip.SampleRate = 30;
		exportClip.IsBindPoseSeed = false;
		exportClip.Tracks.Clear();
		var exportTrack = exportClip.EnsureTrack( "root" );
		WeaponAnimationMath.UpsertKey(
			exportTrack, 0, new Transform( Vector3.Zero ) );
		WeaponAnimationMath.UpsertKey(
			exportTrack, 1, new Transform( new Vector3( 8, 0, 0 ) ) );
		CurveEditingService.ApplyPreset(
			exportTrack,
			[],
			CurveEditorMode.Speed,
			TransformCurveChannel.PositionX,
			CurvePreset.EaseInOut );
		var exportSkeleton = new HostSkeleton();
		exportSkeleton.Add( new HostBone
		{
			Name = "root",
			BindModelTransform = Transform.Zero
		} );
		var firstExport = DmxWriter.WriteAnimation(
			exportDocument, exportSkeleton, exportClip );
		var secondExport = DmxWriter.WriteAnimation(
			exportDocument, exportSkeleton, exportClip );
		Equal(
			report,
			firstExport,
			secondExport,
			"Customized curves must produce deterministic sampled animation output." );
	}

	private static void TestFrameSnapping( WeaponAnimatorSelfTestReport report )
	{
		Near( report, 10.0f / 30.0f, WeaponAnimationMath.SnapTime( 0.34f, 30, false ), 0.0001f, "Frame snapping must select the nearest frame." );
		Near( report, 0.34f, WeaponAnimationMath.SnapTime( 0.34f, 30, true ), 0.0001f, "Subframe keys must preserve time." );
	}

	private static void TestTimelineNavigation( WeaponAnimatorSelfTestReport report )
	{
		var document = WeaponAnimationDocument.CreateDefault( "Timeline navigation" );
		var clip = document.GetSelectedClip()!;
		clip.Duration = 10;
		clip.SampleRate = 30;
		var full = TimelineInteraction.ResolveRange( clip, null );
		Equal( report, 0, full.StartFrame, "A new timeline view must begin at frame zero." );
		Equal( report, 300, full.EndFrame, "A new timeline view must cover the complete clip." );

		var zoomed = TimelineInteraction.Zoom( new TimelineFrameRange( 60, 240 ), 300, true );
		Equal( report, 144, zoomed.Span, "Ctrl+wheel zoom must reduce the visible frame span." );
		Equal(
			report,
			300,
			zoomed.StartFrame + zoomed.EndFrame,
			"Ctrl+wheel zoom must preserve the range midpoint." );
		var panned = TimelineInteraction.Pan( zoomed, 500, 300 );
		Equal( report, 300, panned.EndFrame, "Range panning must clamp at the clip end." );
		var minimum = TimelineInteraction.ResizeStart(
			new TimelineFrameRange( 0, 10 ),
			10,
			300 );
		Equal(
			report,
			TimelineInteraction.MinimumVisibleFrameIntervals,
			minimum.Span,
			"Range handles must retain the minimum two-frame interval." );

		var closeTicks = TimelineInteraction.TickSpacing( 10 );
		var wideTicks = TimelineInteraction.TickSpacing( 0.5f );
		Equal( report, 1, closeTicks.MinorFrames, "Zoomed timelines must expose individual frame ticks." );
		Check(
			report,
			wideTicks.MinorFrames > closeTicks.MinorFrames
				&& wideTicks.MajorFrames > closeTicks.MajorFrames,
			"Tick spacing must become coarser as the visible frame density increases." );
		var marker = TimelineInteraction.KeyMarkerPosition(
			337.42f, 44, 100, 500, TimelineEditorCanvas.TrackHeight );
		Near(
			report,
			337,
			marker.X,
			0.0001f,
			"Key markers must snap horizontally to whole pixels." );
		Near(
			report,
			55,
			marker.Y,
			0.0001f,
			"Key markers must remain vertically centered on their row." );
		Near(
			report,
			105,
			TimelineInteraction.KeyMarkerPosition(
				100, 0, 100, 500, TimelineEditorCanvas.TrackHeight ).X,
			0.0001f,
			"First-frame diamonds must remain fully inside the graph." );
		Near(
			report,
			495,
			TimelineInteraction.KeyMarkerPosition(
				500, 0, 100, 500, TimelineEditorCanvas.TrackHeight ).X,
			0.0001f,
			"Last-frame diamonds must not be covered by the scrollbar gutter." );

		var controller = new WeaponAnimatorController();
		controller.SetDocument( document );
		controller.SetTimelineRange( clip, new TimelineFrameRange( 30, 90 ) );
		controller.SetTimelineVerticalScroll( clip, 132 );
		var state = document.Workspace.GetTimelineView( clip.Id );
		Check(
			report,
			state is not null,
			"Changing a timeline view must create its per-clip workspace state." );
		Near( report, 1, state!.VisibleStart, 0.0001f, "Timeline range start must persist in seconds." );
		Near( report, 3, state.VisibleEnd, 0.0001f, "Timeline range end must persist in seconds." );
		Near( report, 132, state.VerticalScroll, 0.0001f, "Vertical track scroll must persist per clip." );

		clip.Tracks.Add( new TransformTrack { Target = "one" } );
		clip.Tracks.Add( new TransformTrack { Target = "two" } );
		document.Rig.VisibilityParts.Add( new WeaponVisibilityPart() );
		Equal(
			report,
			4,
			TimelineInteraction.TrackRowCount( document, clip ),
			"Timeline row count must include every transform track, visibility track, and the tag row." );
	}

	private static void TestTimelineSelectionAndMovement( WeaponAnimatorSelfTestReport report )
	{
		var first = Guid.NewGuid();
		var second = Guid.NewGuid();
		var third = Guid.NewGuid();
		var replaced = TimelineInteraction.CombineKeySelection(
			[first],
			[second, third],
			additive: false,
			toggle: false );
		Check(
			report,
			replaced.SetEquals( [second, third] ),
			"A plain marquee must replace the previous key selection." );
		var added = TimelineInteraction.CombineKeySelection(
			[first],
			[second],
			additive: true,
			toggle: false );
		Check(
			report,
			added.SetEquals( [first, second] ),
			"Shift-marquee must add intersected keys." );
		var toggled = TimelineInteraction.CombineKeySelection(
			[first, second],
			[second, third],
			additive: false,
			toggle: true );
		Check(
			report,
			toggled.SetEquals( [first, third] ),
			"Ctrl-marquee must toggle every intersected key." );
		var scrolledMarquee = TimelineInteraction.ProjectMarquee(
			startX: 220,
			startContentY: 400,
			currentX: 520,
			currentContentY: 290,
			verticalScroll: 40,
			minimumX: 180,
			maximumX: 500 );
		Near(
			report,
			360,
			scrolledMarquee.Bottom,
			0.0001f,
			"A marquee start must remain anchored to its original track while scrolling." );
		Near(
			report,
			250,
			scrolledMarquee.Top,
			0.0001f,
			"A scrolling marquee endpoint must follow the newly revealed content." );
		Near(
			report,
			500,
			scrolledMarquee.Right,
			0.0001f,
			"A marquee must remain clipped to the graph's right edge." );
		Equal(
			report,
			-2,
			TimelineInteraction.ClampGroupFrameDelta( [2, 5], -20, 30 ),
			"Moving keys before frame zero must clamp the group as a unit." );
		Equal(
			report,
			25,
			TimelineInteraction.ClampGroupFrameDelta( [2, 5], 40, 30 ),
			"Moving keys past the clip end must preserve their internal spacing." );

		var document = WeaponAnimationDocument.CreateDefault( "Timeline key move" );
		var clip = document.GetSelectedClip()!;
		clip.Duration = 1;
		clip.SampleRate = 30;
		var track = clip.EnsureTrack( "weapon_root" );
		var keyA = WeaponAnimationMath.UpsertKey( track, 2f / 30, Transform.Zero );
		var keyB = WeaponAnimationMath.UpsertKey( track, 5f / 30, Transform.Zero );
		WeaponAnimationMath.UpsertKey( track, 7f / 30, Transform.Zero );
		var controller = new WeaponAnimatorController();
		controller.SetDocument( document );
		controller.SetSelectedKeys( [keyA.Id, keyB.Id] );
		var starts = new Dictionary<Guid, float>
		{
			[keyA.Id] = keyA.Time,
			[keyB.Id] = keyB.Time
		};
		controller.BeginSelectedKeyMove();
		controller.UpdateSelectedKeyMove( starts, 2 );
		controller.EndSelectedKeyMove( starts, 2 );
		Equal(
			report,
			2,
			track.Keys.Count,
			"A moved key must replace an unselected key occupying its destination frame." );
		Check(
			report,
			track.Keys.Select( x => TimelineInteraction.TimeToFrame( x.Time, 30 ) )
				.SequenceEqual( [4, 7] ),
			"Selected keys must move by the same snapped frame delta." );
		controller.Undo();
		clip = controller.Document.GetSelectedClip()!;
		Equal(
			report,
			3,
			clip.EnsureTrack( "weapon_root" ).Keys.Count,
			"A complete key drag must undo as one action." );

		var deleteDocument = WeaponAnimationDocument.CreateDefault( "Timeline key delete" );
		var deleteClip = deleteDocument.GetSelectedClip()!;
		var deleteTransformKey = WeaponAnimationMath.UpsertKey(
			deleteClip.EnsureTrack( "weapon_root" ),
			0,
			Transform.Zero );
		var visibilityPart = new WeaponVisibilityPart { Name = "Magazine" };
		deleteDocument.Rig.VisibilityParts.Add( visibilityPart );
		var deleteVisibilityKey = new VisibilityKey { Time = 0, Visible = false };
		deleteClip.EnsureVisibilityTrack( visibilityPart.Id ).Keys.Add( deleteVisibilityKey );
		var deleteController = new WeaponAnimatorController();
		deleteController.SetDocument( deleteDocument );
		deleteController.SetSelectedKeys( [deleteTransformKey.Id, deleteVisibilityKey.Id] );
		deleteController.DeleteSelectedKeys();
		deleteClip = deleteController.Document.GetSelectedClip()!;
		Equal(
			report,
			0,
			deleteClip.Tracks.SelectMany( x => x.Keys ).Count(),
			"Deleting selected keys must remove transform keys." );
		Equal(
			report,
			0,
			deleteClip.VisibilityTracks.SelectMany( x => x.Keys ).Count(),
			"Deleting selected keys must remove visibility keys." );
		Equal(
			report,
			0,
			deleteController.SelectedKeys.Count,
			"Deleting keys must clear the stale key selection." );
		deleteController.Undo();
		deleteClip = deleteController.Document.GetSelectedClip()!;
		Equal(
			report,
			2,
			deleteClip.Tracks.SelectMany( x => x.Keys ).Count()
				+ deleteClip.VisibilityTracks.SelectMany( x => x.Keys ).Count(),
			"Deleting a mixed key selection must undo as one action." );
	}

	private static void TestTimelineKeyReversal( WeaponAnimatorSelfTestReport report )
	{
		var document = WeaponAnimationDocument.CreateDefault( "Timeline reverse" );
		var clip = document.GetSelectedClip()!;
		clip.Duration = 1;
		clip.SampleRate = 30;
		var track = clip.EnsureTrack( "weapon_root" );
		track.Interpolation = TrackInterpolation.Linear;
		var start = WeaponAnimationMath.UpsertKey(
			track,
			0,
			new Transform( new Vector3( 0, 0, 0 ), Rotation.Identity, Vector3.One ) );
		var end = WeaponAnimationMath.UpsertKey(
			track,
			1,
			new Transform( new Vector3( 10, 0, 0 ), Rotation.Identity, Vector3.One ) );
		start.CurveTangents.PositionOut = new Vector3( 4, 0, 0 );
		end.CurveTangents.PositionIn = new Vector3( 12, 0, 0 );
		var span = track.EnsureCurveSpan( start.Id, end.Id );
		span.CustomChannels = TransformCurveChannel.PositionX;
		span.HasSpeedCurve = true;
		span.Speed = new MotionRateCurve
		{
			StartRate = 0.4f,
			EndRate = 1.6f,
			StartSlope = 0.5f,
			EndSlope = -0.25f,
			StartHandleMode = CurveHandleMode.Free,
			EndHandleMode = CurveHandleMode.Aligned
		};
		var sampleTimes = new[] { 0.0f, 0.2f, 0.5f, 0.8f, 1.0f };
		var sourceSamples = sampleTimes
			.Select( x => WeaponAnimationMath.SampleTrack( track, x, Transform.Zero ).Position )
			.ToArray();

		var visibilityPart = new WeaponVisibilityPart { Name = "Magazine" };
		document.Rig.VisibilityParts.Add( visibilityPart );
		var visibility = clip.EnsureVisibilityTrack( visibilityPart.Id );
		var hidden = new VisibilityKey { Time = 0, Visible = false };
		var shown = new VisibilityKey { Time = 1, Visible = true };
		visibility.Keys.AddRange( [hidden, shown] );

		var controller = new WeaponAnimatorController();
		controller.SetDocument( document );
		controller.ReverseKeys();
		clip = controller.Document.GetSelectedClip()!;
		track = clip.EnsureTrack( "weapon_root" );
		Equal(
			report,
			end.Id,
			track.Keys[0].Id,
			"With no key selection, Reverse must flip all transform keys across the clip." );
		Equal(
			report,
			start.Id,
			track.Keys[^1].Id,
			"Whole-clip reversal must place the first key at the last frame." );
		var reversedSpan = track.FindCurveSpan( end.Id, start.Id );
		Check(
			report,
			reversedSpan is not null,
			"Custom curve spans must reverse with their endpoint keys." );
		if ( reversedSpan is not null )
		{
			Near(
				report,
				span.Speed.EndRate,
				reversedSpan.Speed.StartRate,
				0.0001f,
				"Reversing a speed curve must swap its endpoint rates." );
			Near(
				report,
				-span.Speed.EndSlope,
				reversedSpan.Speed.StartSlope,
				0.0001f,
				"Reversing a speed curve must invert its former end slope." );
			Equal(
				report,
				span.Speed.EndHandleMode,
				reversedSpan.Speed.StartHandleMode,
				"Reversing a speed curve must swap its handle modes." );
		}
		Near(
			report,
			-12,
			track.Keys[0].CurveTangents.PositionOut.x,
			0.0001f,
			"Reversed channel curves must negate the former incoming tangent." );
		Near(
			report,
			-4,
			track.Keys[^1].CurveTangents.PositionIn.x,
			0.0001f,
			"Reversed channel curves must negate the former outgoing tangent." );
		for ( var i = 0; i < sampleTimes.Length; i++ )
		{
			Near(
				report,
				sourceSamples[^(i + 1)],
				WeaponAnimationMath.SampleTrack(
					track,
					sampleTimes[i],
					Transform.Zero ).Position,
				0.01f,
				"Reversed custom curves must reproduce the original motion backward." );
		}
		visibility = clip.EnsureVisibilityTrack( visibilityPart.Id );
		Equal(
			report,
			shown.Id,
			visibility.Keys[0].Id,
			"Whole-clip reversal must also flip visibility keys." );

		controller.Undo();
		clip = controller.Document.GetSelectedClip()!;
		track = clip.EnsureTrack( "weapon_root" );
		Equal(
			report,
			start.Id,
			track.Keys[0].Id,
			"Transform, curve, and visibility reversal must undo as one action." );

		var selectionDocument = WeaponAnimationDocument.CreateDefault( "Selected reverse" );
		var selectionClip = selectionDocument.GetSelectedClip()!;
		selectionClip.Duration = 2;
		selectionClip.SampleRate = 10;
		var selectionTrack = selectionClip.EnsureTrack( "slide" );
		var first = WeaponAnimationMath.UpsertKey(
			selectionTrack,
			0.2f,
			new Transform( new Vector3( 2, 0, 0 ), Rotation.Identity, Vector3.One ) );
		var middle = WeaponAnimationMath.UpsertKey(
			selectionTrack,
			0.6f,
			new Transform( new Vector3( 6, 0, 0 ), Rotation.Identity, Vector3.One ) );
		var last = WeaponAnimationMath.UpsertKey(
			selectionTrack,
			1.0f,
			new Transform( new Vector3( 10, 0, 0 ), Rotation.Identity, Vector3.One ) );
		var selectionController = new WeaponAnimatorController();
		selectionController.SetDocument( selectionDocument );
		selectionController.SetSelectedKeys( [first.Id, last.Id] );
		selectionController.ReverseKeys();
		selectionTrack = selectionController.Document.GetSelectedClip()!.EnsureTrack( "slide" );
		Equal(
			report,
			last.Id,
			selectionTrack.Keys[0].Id,
			"Selected reversal must flip keys around the selected range, not the clip bounds." );
		Equal(
			report,
			middle.Id,
			selectionTrack.Keys[1].Id,
			"Keys outside the reversed selection must retain their frame." );
		Equal(
			report,
			first.Id,
			selectionTrack.Keys[2].Id,
			"Selected reversal must preserve key identities and selection." );
		Check(
			report,
			selectionController.SelectedKeys.ToHashSet().SetEquals( [first.Id, last.Id] ),
			"Reversed keys must remain selected for immediate follow-up editing." );
	}

	private static void TestTimelinePlayback( WeaponAnimatorSelfTestReport report )
	{
		var document = WeaponAnimationDocument.CreateDefault( "Timeline playback" );
		var clip = document.GetSelectedClip()!;
		clip.Duration = 1;
		clip.SampleRate = 30;
		clip.Loop = false;
		var controller = new WeaponAnimatorController();
		controller.SetDocument( document );

		controller.SetTimelineTime( 0.01f );
		Near( report, 0, document.Workspace.TimelineTime, 0.0001f, "Timeline seeking must reject fractional-frame positions." );
		controller.SetTimelineTime( 0.02f );
		Near( report, 1f / 30, document.Workspace.TimelineTime, 0.0001f, "Timeline seeking must snap to the nearest whole frame." );
		controller.JumpToLastFrame();
		controller.TogglePlayback();
		Check( report, controller.IsPlaying, "Play must enter the shared playback state." );
		Near( report, 0, document.Workspace.TimelineTime, 0.0001f, "Playing from the last frame must restart at frame zero." );
		controller.AdvancePlayback( 0.04f );
		Equal(
			report,
			1,
			TimelineInteraction.TimeToFrame( document.Workspace.TimelineTime, 30 ),
			"Playback must advance through whole-frame preview positions." );
		controller.StepTimelineFrame( 1 );
		Check( report, !controller.IsPlaying, "Manual frame stepping must pause playback." );
		Equal(
			report,
			2,
			TimelineInteraction.TimeToFrame( document.Workspace.TimelineTime, 30 ),
			"Next-frame controls must advance exactly one frame." );
		controller.JumpToLastFrame();
		controller.StepTimelineFrame( 1 );
		Equal(
			report,
			30,
			TimelineInteraction.TimeToFrame( document.Workspace.TimelineTime, 30 ),
			"Frame stepping must clamp at the final frame." );
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
		var selectionEvents = 0;
		var keySelectionEvents = 0;
		controller.DocumentChanged += () => documentEvents++;
		controller.PoseChanged += () => poseEvents++;
		controller.SelectionChanged += () => selectionEvents++;
		controller.KeySelectionChanged += () => keySelectionEvents++;
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
		var selectionBeforeKeys = selectionEvents;
		controller.SelectKeys( [key.Id], false );
		Equal(
			report,
			selectionBeforeKeys,
			selectionEvents,
			"Key selection must not broadcast a control-selection rebuild." );
		Check(
			report,
			keySelectionEvents > 0,
			"Key selection must publish its dedicated lightweight event." );
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

		var keyController = new WeaponAnimatorController();
		var keyDocument = ValidDocument();
		keyController.SetDocument( keyDocument );
		keyController.SelectBone( "weapon_root" );
		keyController.SetTimelineTime( 0.5f );
		keyController.KeySelectedTransform();
		Check(
			report,
			keyController.Document.GetSelectedClip()!.Tracks
				.Single( current => current.Target == "weapon_root" )
				.Keys.Any( current => MathF.Abs( current.Time - 0.5f ) < 0.0001f ),
			"The shared K/Add Key command must key a selected weapon bone." );
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
		document.Source.SourcePath = "weapons/test/source.smd";
		var smdValidation = WeaponAnimationValidator.ValidateForGeneration( document );
		Check(
			report,
			smdValidation.Issues.Any( issue =>
				issue.Blocking && issue.Code == "source.not_embeddable" ),
			"SMD projects must explain the ModelDoc generation limitation before Generate runs." );
		Check(
			report,
			smdValidation.Issues.Any( issue =>
				issue.Code == "source.not_embeddable"
				&& issue.Message.Contains( "SMD", StringComparison.Ordinal ) ),
			"The generation-format diagnostic must name the unsupported source extension." );
		document.Source.SourcePath = "weapons/test/source.vmdl";
		Check(
			report,
			WeaponAnimationValidator.ValidateForGeneration( document ).Issues.All( issue =>
				issue.Code != "source.not_embeddable" ),
			"VMDL projects must pass source-format validation through the generated adapter path." );
		document.Source.SourcePath = "weapons/test/source.fbx";
		document.Calibration.Anchors.RemoveAll( anchor =>
			anchor.Kind is AnchorKind.RearBore or AnchorKind.FrontBore );
		Check(
			report,
			WeaponAnimationValidator.ValidateCalibration( document ).IsValid,
			"Auto-align markers must not block an already-oriented weapon." );

		document.Source.OriginalModelDimensions = Vector3.One;
		document.Calibration.PhysicalTransform =
			document.Calibration.PhysicalTransform.WithScale( 1 );
		Check(
			report,
			WeaponAnimationValidator.ValidateCalibration( document ).Issues.Any( issue =>
				issue.Code == "scale.implausible" ),
			"Implausible-scale validation must use persisted source bounds without requiring a measurement." );

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

	private static void TestGenerationOutputPaths( WeaponAnimatorSelfTestReport report )
	{
		var contentRoot = Path.Combine(
			Path.GetTempPath(),
			$"weaponanim-output-{Guid.NewGuid():N}",
			"Assets" );
		var document = WeaponAnimationDocument.CreateDefault( "Output Test" );
		var defaultOutput = AssetGenerationService.ResolveOutputRootForContentRoot(
			document,
			contentRoot );
		Equal(
			report,
			Path.GetFullPath( Path.Combine(
				contentRoot,
				"weapons",
				"output_test",
				"viewmodel" ) ),
			defaultOutput,
			"Default generation output must resolve beneath Assets even before the folder exists." );

		document.Output.OutputFolder = "/weapons/custom/viewmodel";
		Equal(
			report,
			Path.GetFullPath( Path.Combine(
				contentRoot,
				"weapons",
				"custom",
				"viewmodel" ) ),
			AssetGenerationService.ResolveOutputRootForContentRoot( document, contentRoot ),
			"A leading asset slash must remain a project-relative output path." );

		document.Output.OutputFolder = "../outside";
		var rejectedEscape = false;
		try
		{
			AssetGenerationService.ResolveOutputRootForContentRoot( document, contentRoot );
		}
		catch ( InvalidOperationException )
		{
			rejectedEscape = true;
		}
		Check(
			report,
			rejectedEscape,
			"Generation output must reject paths that escape the project's Assets folder." );

		document.Output.OutputFolder = "C:/outside";
		var rejectedDrive = false;
		try
		{
			AssetGenerationService.ResolveOutputRootForContentRoot( document, contentRoot );
		}
		catch ( InvalidOperationException )
		{
			rejectedDrive = true;
		}
		Check(
			report,
			rejectedDrive,
			"Generation output must reject absolute drive paths on every host platform." );

		var nestedOutputRoot = Path.Combine(
			Path.GetTempPath(),
			$"weaponanim-nested-output-{Guid.NewGuid():N}" );
		try
		{
			AssetGenerationService.WriteTextSourcesForTests(
				nestedOutputRoot,
				new Dictionary<string, string>
				{
					["materials/output_test_body.vmat"] = "fixture material",
					["output_test_vm.vmdl"] = "fixture model"
				} );
			Check(
				report,
				File.Exists( Path.Combine(
					nestedOutputRoot,
					"materials",
					"output_test_body.vmat" ) ),
				"Generation must create parent directories for nested material sources." );
		}
		finally
		{
			if ( Directory.Exists( nestedOutputRoot ) )
				Directory.Delete( nestedOutputRoot, true );
		}

		document.Output = null!;
		Equal(
			report,
			Path.GetFullPath( Path.Combine(
				contentRoot,
				"weapons",
				"output_test",
				"viewmodel" ) ),
			AssetGenerationService.ResolveOutputRootForContentRoot( document, contentRoot ),
			"Generation must repair missing output settings instead of throwing." );
	}

	private static void TestGeneratedFileRemoval( WeaponAnimatorSelfTestReport report )
	{
		var root = Path.Combine(
			Path.GetTempPath(),
			$"weaponanim-removal-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( root );
		try
		{
			var host = Path.Combine( root, "weapon_host.vmdl" );
			var clip = Path.Combine( root, "weapon_idle.dmx" );
			var graph = Path.Combine( root, "weapon.vanmgrph" );
			var prefab = Path.Combine( root, "v_weapon.prefab" );
			foreach ( var file in new[] { host, clip, graph, prefab } )
			{
				File.WriteAllText( file, "generated" );
				File.WriteAllText( $"{file}_c", "compiled" );
			}

			AssetGenerationService.DeleteGeneratedFiles( [clip, host, graph, prefab] );

			Check(
				report,
				!File.Exists( host ) && !File.Exists( $"{host}_c" ),
				"Removing a generated asset must take its compiled artifact with it." );
			Check(
				report,
				!File.Exists( clip ) && !File.Exists( graph ) && !File.Exists( prefab ),
				"Every listed generated file must be removed." );

			// The dependant .vmdl has to be gone before its .dmx sources, or the asset system
			// keeps recompiling a model whose animation dependencies stopped existing.
			File.WriteAllText( host, "generated" );
			File.WriteAllText( clip, "generated" );
			var ordered = AssetGenerationService.OrderForRemoval( [clip, host] ).ToList();
			Equal(
				report,
				host,
				ordered[0],
				"Compiled dependants must be removed before the sources they consume." );
				var lifecycleFiles = new[]
				{
					"weapon_sequence_idle.dmx",
					"weapon_source_adapter.vmdl",
					"weapon_vm_bootstrap.vmdl",
					"weapon.vanmgrph",
				"weapon_vm.vmdl",
				"v_weapon.prefab"
			};
			var writeOrder = AssetGenerationService.OrderForWrite( lifecycleFiles ).ToList();
			Equal(
				report,
				string.Join( "|", lifecycleFiles ),
				string.Join( "|", writeOrder ),
				"Generated sources must appear in dependency order so automatic compilation never observes a missing preview host." );
			var removeOrder = AssetGenerationService.OrderForRemoval( lifecycleFiles ).ToList();
			Equal(
					report,
					"v_weapon.prefab|weapon_vm.vmdl|weapon.vanmgrph|weapon_vm_bootstrap.vmdl|weapon_source_adapter.vmdl|weapon_sequence_idle.dmx",
				string.Join( "|", removeOrder ),
				"Generated consumers must be removed in reverse dependency order." );
			Check(
				report,
				!AssetGenerationService.ShouldDeleteCreatedFileOnRollback(
					"weapon_sprint.dmx",
					previouslyOwned: true ),
				"Rollback must retain a recreated owned DMX dependency needed by an older host." );
			Check(
				report,
				AssetGenerationService.ShouldDeleteCreatedFileOnRollback(
					"weapon_vm.vmdl",
					previouslyOwned: true )
				&& AssetGenerationService.ShouldDeleteCreatedFileOnRollback(
					"new_clip.dmx",
					previouslyOwned: false ),
				"Rollback must remove failed compiled consumers and newly introduced dependencies." );

			var freshnessSource = Path.Combine( root, "freshness.vmdl" );
			var freshnessCompiled = freshnessSource + "_c";
			File.WriteAllText( freshnessSource, "source" );
			File.WriteAllText( freshnessCompiled, "compiled" );
			var now = DateTime.UtcNow;
			File.SetLastWriteTimeUtc( freshnessSource, now );
			File.SetLastWriteTimeUtc( freshnessCompiled, now.AddSeconds( 1 ) );
			Check(
				report,
				AssetGenerationService.IsFreshCompiledArtifact(
					freshnessSource,
					freshnessCompiled ),
				"A newly written compiled artifact must complete generation even while its managed Asset wrapper is stale." );
			File.SetLastWriteTimeUtc( freshnessCompiled, now.AddSeconds( -10 ) );
			Check(
				report,
				!AssetGenerationService.IsFreshCompiledArtifact(
					freshnessSource,
					freshnessCompiled ),
				"An artifact older than its regenerated source must never be accepted as compile success." );
		}
		finally
		{
			Directory.Delete( root, true );
		}
	}

	private static void TestMaterialPipeline( WeaponAnimatorSelfTestReport report )
	{
		var embeddedMaterials = WeaponMaterialPipeline.MatchEmbeddedMaterialNamesForTests(
			["HK_P30L", "cartridge"],
			["Material", "H&K_P30L", "cartridge", "cartridge_BaseColor"] );
		Check(
			report,
			embeddedMaterials.Contains( "H&K_P30L" )
				&& embeddedMaterials.Contains( "cartridge" )
				&& embeddedMaterials.Count == 2,
			"Embedded FBX labels must preserve special characters when matching texture-set names." );

		var discovered = WeaponMaterialPipeline.DiscoverForTests(
			[
				"H&K_P30L.vmat",
				"cartridge.vmat",
				"materials/error.vmat"
			],
			[
				"/fixture/Textures/HK_P30L_BaseColor.png",
				"/fixture/Textures/HK_P30L_Normal_GL.png",
				"/fixture/Textures/HK_P30L_Normal_DX.png",
				"/fixture/Textures/HK_P30L_Roughness.png",
				"/fixture/Textures/HK_P30L_Metallic.png",
				"/fixture/Textures/cartridge_BaseColor.png",
				"/fixture/Textures/cartridge_Normal_DX.png"
			] );
		Equal(
			report,
			2,
			discovered.Count,
			"Nearby texture discovery must retain every FBX material slot." );
		var pistol = discovered.Single( material => material.Name == "H&K_P30L" );
		Check(
			report,
			pistol.FindTexture( WeaponTextureChannel.Normal )?.AssetPath
				.EndsWith( "Normal_GL.png", StringComparison.OrdinalIgnoreCase ) == true,
			"S&box-compatible OpenGL normal maps must win when both GL and DX variants are available." );
		Check(
			report,
			pistol.FindTexture( WeaponTextureChannel.Metalness ) is not null
				&& discovered.Single( material => material.Name == "cartridge" )
					.FindTexture( WeaponTextureChannel.BaseColor ) is not null,
			"Texture sets must be matched independently to their source material names." );
		Check(
			report,
			discovered.All( material => !material.SourceMaterialPath.Equals(
				"materials/error",
				StringComparison.OrdinalIgnoreCase ) ),
			"The compiler error material must never become a generated weapon material slot." );
		Check(
			report,
			discovered.All( material => !Path.HasExtension( material.SourceMaterialPath ) ),
			"Stored source material labels must not look like GameResource dependencies." );
		Equal(
			report,
			"weaponanim_preview_cache/0123456789abcdef",
			WeaponMaterialPipeline.LegalPreviewRelativeRootForTests(
				"/fixture/Assets/.weaponanim-cache/0123456789abcdef" ),
			"Preview materials must use a legal non-hidden asset namespace." );
		var originalRevision = WeaponMaterialPipeline.PreviewRevision( discovered );
		pistol.Textures[0].Sha256 = "changed-image-hash";
		var changedRevision = WeaponMaterialPipeline.PreviewRevision( discovered );
		Check(
			report,
			!originalRevision.Equals( changedRevision, StringComparison.Ordinal ),
			"A changed texture input must create a new immutable preview revision." );
		pistol.Textures[0].Sha256 = "";

		var document = ValidDocument();
		document.Source.Materials = discovered.ToList();
		var generated = WeaponMaterialPipeline.BuildOutputTextFiles(
			document,
			"weapons/test_weapon/viewmodel" );
		var material = generated["materials/test_weapon_h_k_p30l.vmat"];
		Check(
			report,
			material.Contains( "F_SPECULAR 1", StringComparison.Ordinal )
				&& material.Contains( "F_METALNESS_TEXTURE 1", StringComparison.Ordinal )
				&& material.Contains( "TextureMetalness", StringComparison.Ordinal )
				&& material.Contains(
					"test_weapon_h_k_p30l_metalness.png",
					StringComparison.Ordinal ),
			"Generated weapon VMATs must enable specular and mapped metalness." );
		Check(
			report,
			generated.Keys.Count( path => path.EndsWith(
				".vtex",
				StringComparison.OrdinalIgnoreCase ) ) == 0
				&& material.Contains(
					"test_weapon_h_k_p30l_color.png",
					StringComparison.Ordinal )
				&& material.Contains(
					"test_weapon_h_k_p30l_normal.png",
					StringComparison.Ordinal ),
			"VMATs must reference image inputs directly so S&box can build native generated VTEX resources." );
		document.Source.NeedsModelDocWrapper = true;
		pistol.PreviewMaterialPath =
			".weaponanim-cache/fixture/materials/h_k_p30l.vmat";
		Check(
			report,
			WeaponMaterialPipeline.RequiresPreviewRefresh( document ),
			"Legacy hidden preview material paths must force a safe material refresh." );
		foreach ( var binding in discovered.Where( binding => binding.HasUsableTextures ) )
		{
			binding.PreviewMaterialPath =
				$"weaponanim_preview_cache/fixture/revision/materials/{binding.OutputName}.vmat";
		}
		Check(
			report,
			!WeaponMaterialPipeline.RequiresPreviewRefresh( document ),
			"Legal compiled preview material paths must not refresh repeatedly." );
		var serializedDocument = Json.Serialize( document );
		Check(
			report,
			!serializedDocument.Contains(
				"PreviewMaterialPath",
				StringComparison.Ordinal )
				&& !serializedDocument.Contains(
					"H&K_P30L.vmat",
					StringComparison.OrdinalIgnoreCase ),
			"Transient preview VMATs and source slot extensions must stay out of .wepanim serialization." );

		var legacyMaterialDocument = WeaponAnimationDocument.CreateDefault();
		legacyMaterialDocument.Source.Materials =
		[
			new SourceMaterialBinding
			{
				SourceMaterialPath = "cartridge.vmat",
				Name = "cartridge",
				OutputName = "cartridge"
			}
		];
		var materialMigration = WeaponAnimationMigration.MigrateAndRepair(
			legacyMaterialDocument );
		Check(
			report,
			materialMigration.RepairedMaterialMetadata
				&& legacyMaterialDocument.Source.Materials[0].SourceMaterialPath
					.Equals( "cartridge", StringComparison.Ordinal ),
			"Opening an existing project must remove false VMAT dependencies from source slot metadata." );

		var recoveredCandidate = WeaponSourceImporter.SelectRecoveryCandidateForTests(
		[
			new(
				"/preview/newer-uncompiled/models/source_abc_textured.vmdl",
				new DateTime( 2026, 7, 28, 20, 0, 0, DateTimeKind.Utc ),
				false,
				true ),
			new(
				"/preview/legacy/models/source_abc_textured.vmdl",
				new DateTime( 2026, 7, 28, 19, 0, 0, DateTimeKind.Utc ),
				true,
				false ),
			new(
				"/preview/versioned/models/source_abc_textured.vmdl",
				new DateTime( 2026, 7, 28, 18, 0, 0, DateTimeKind.Utc ),
				true,
				true )
		] );
		Equal(
			report,
			"/preview/versioned/models/source_abc_textured.vmdl",
			recoveredCandidate,
			"Missing saved source wrappers must recover to a compiled immutable preview revision." );
		Check(
			report,
			!WeaponAnimatorViewport.ShouldRetryMissingSourcePreview(
				"weaponanim_preview_cache/document/source.vmdl",
				"weaponanim_preview_cache/document/source.vmdl" )
				&& WeaponAnimatorViewport.ShouldRetryMissingSourcePreview(
					"weaponanim_preview_cache/document/repaired.vmdl",
					"weaponanim_preview_cache/document/source.vmdl" ),
			"A failed source load must not rebuild the private scene every frame, "
				+ "but a repaired path must trigger one rebuild." );

		var remaps = WeaponMaterialPipeline.OutputRemaps(
			document,
			"weapons/test_weapon/viewmodel" );
		Equal(
			report,
			2,
			remaps.Count,
			"Final generation must preserve separate material-slot remaps." );
		var host = ModelDocWriter.WriteHost(
			"host_reference.dmx",
			[],
			"",
			["weapon_root"],
			new HostWeaponMesh(
				"source.fbx",
				"weapon_root",
				Transform.Zero,
				[],
				remaps ) );
		Check(
			report,
			host.Contains( "use_global_default = false", StringComparison.Ordinal )
				&& !host.Contains( "use_global_default = true", StringComparison.Ordinal )
				&& host.Contains( "from = \"H&K_P30L.vmat\"", StringComparison.Ordinal )
				&& host.Contains( "from = \"cartridge.vmat\"", StringComparison.Ordinal ),
			"Weapon ModelDocs must use per-slot remaps with global material override disabled." );
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
		Check(
			report,
			first.Contains( "\"forwardParity\" \"int\" \"1\"", StringComparison.Ordinal )
				&& !first.Contains( "\"forwardParity\" \"int\" \"-2\"", StringComparison.Ordinal ),
			"Reference DMX must use the Source 2 Z-up axis parity expected by ModelDoc." );
		Equal( report, first, second, "DMX host references must be deterministic." );
		var document = WeaponAnimationDocument.CreateDefault();
		document.Binding.Configuration = GripConfiguration.OneHanded;
		var clip = document.EnsureClip( WeaponClipRole.Idle );
		clip.Duration = 1;
		clip.SampleRate = 30;
		var animation = DmxWriter.WriteAnimation( document, skeleton, clip );
		Check(
			report,
			animation.Contains( "\"DmeChannelsClip\"", StringComparison.Ordinal )
				&& animation.Contains( "\"DmeVector3LogLayer\"", StringComparison.Ordinal )
				&& animation.Contains( "\"DmeQuaternionLogLayer\"", StringComparison.Ordinal )
				&& animation.Contains( "\"DmeFloatLogLayer\"", StringComparison.Ordinal ),
			"DMX animation output must contain position, rotation, and scale channels." );
		Check(
			report,
			animation.Contains( "\"DmeJoint\"", StringComparison.Ordinal )
				&& !animation.Contains( "\"DmeDag\"", StringComparison.Ordinal ),
			"Animation skeleton entries must be Source 2 joints rather than generic DAG nodes." );
		Check(
			report,
			animation.Contains( "\"DmeTransformList\"", StringComparison.Ordinal )
				&& animation.Contains( "\"baseStates\" \"element_array\"", StringComparison.Ordinal ),
			"Animation DMX must include a bind transform list for ModelDoc sequence import." );
		Check(
			report,
			animation.Contains( "\"mode\" \"int\" \"1\"", StringComparison.Ordinal ),
			"Animation channels must use the Source 2 exporter channel mode." );
		Check(
			report,
			animation.Contains( "\"jointList\" \"element_array\"", StringComparison.Ordinal )
				&& animation.Contains(
					"\"element\" \"" + DmxAnimationJointIdForTest( clip, 0 ) + "\"",
					StringComparison.Ordinal ),
			"Animation DMX must register every animated joint with its model." );
		Check(
			report,
			animation.Contains( "\t\t\"1\"\n", StringComparison.Ordinal ),
			"A one-second animation must include its final sample time." );
		Check(
			report,
			!animation.Contains( "NaN", StringComparison.OrdinalIgnoreCase )
				&& !animation.Contains( "Infinity", StringComparison.OrdinalIgnoreCase ),
			"DMX animation output must contain finite transforms." );
		Check(
			report,
			animation.Contains( "\"forwardParity\" \"int\" \"1\"", StringComparison.Ordinal )
				&& !animation.Contains( "\"forwardParity\" \"int\" \"-2\"", StringComparison.Ordinal ),
			"Animation DMX must use the same Source 2 axis system as its host reference." );
		Equal(
			report,
			animation,
			DmxWriter.WriteAnimation( document, skeleton, clip ),
			"DMX animation output must be deterministic." );

		var scaledSkeleton = new HostSkeleton();
		scaledSkeleton.Add( new HostBone
		{
			Name = "root",
			BindModelTransform = Transform.Zero,
			BindLocalTransform = Transform.Zero,
			HasExplicitBindLocal = true
		} );
		scaledSkeleton.Add( new HostBone
		{
			Name = "weapon_root",
			ParentName = "root",
			BindModelTransform = new Transform(
				Vector3.Zero,
				Rotation.Identity,
				Vector3.One * 0.56f ),
			BindLocalTransform = new Transform(
				Vector3.Zero,
				Rotation.Identity,
				Vector3.One * 0.56f ),
			HasExplicitBindLocal = true,
			IsWeaponBone = true
		} );
		scaledSkeleton.Add( new HostBone
		{
			Name = "hammer",
			ParentName = "weapon_root",
			BindModelTransform = new Transform(
				new Vector3( 0, -5.75f, 0.2f ) * 0.56f ),
			BindLocalTransform = new Transform( new Vector3( 0, -5.75f, 0.2f ) ),
			HasExplicitBindLocal = true,
			IsWeaponBone = true
		} );
		var hammerTrack = clip.EnsureTrack( "hammer" );
		var hammerRotation = Rotation.FromPitch( 45 );
		WeaponAnimationMath.UpsertKey(
			hammerTrack,
			0,
			new Transform( new Vector3( 0, -5.75f, 0.2f ), hammerRotation ) );
		var scaledPose = AnimationPoseEvaluator.Evaluate(
			document,
			scaledSkeleton,
			clip,
			0 );
		var exportedPose = DmxWriter.BuildCompilerPoseLocals(
			scaledSkeleton,
			scaledPose.Local );
		var exportedRoot = exportedPose["weapon_root"];
		var exportedHammer = exportedPose["hammer"];
		Near(
			report,
			Vector3.One,
			exportedRoot.Scale,
			0.0001f,
			"Animation export must use ModelDoc's scale-one compiled bind space." );
		Near(
			report,
			new Vector3( 0, -5.75f, 0.2f ) * 0.56f,
			exportedHammer.Position,
			0.0001f,
			"Rotating a weapon child must use the physical scale-baked mesh pivot in compiled bind space." );
		Near(
			report,
			hammerRotation.Forward,
			exportedHammer.Rotation.Forward,
			0.0001f,
			"Rotating a weapon child must retain its authored local rotation in compiled bind space." );
		var scaledAnimation = DmxWriter.WriteAnimation(
			document,
			scaledSkeleton,
			clip );
		var scaledReference = DmxWriter.WriteReference( scaledSkeleton );
		Check(
			report,
			!scaledAnimation.Contains(
				"\"scale\" \"float\" \"0.56\"",
				StringComparison.Ordinal ),
			"Animation bind declarations must not reintroduce source scale after ModelDoc bakes it into the host." );
		Check(
			report,
			scaledReference.Contains(
				"\"position\" \"vector3\" \"0 -3.22 0.112\"",
				StringComparison.Ordinal )
				&& scaledAnimation.Contains(
					"\"position\" \"vector3\" \"0 -3.22 0.112\"",
					StringComparison.Ordinal ),
			"Reference and animation skeletons must share the scale-baked physical pivot of rotating weapon children." );

		document.Manifest.Files.Add( new GeneratedFileRecord
		{
			RelativePath = "generated_sequence.dmx"
		} );
		Check(
			report,
			!Json.Serialize( document ).Contains( "\"Manifest\"", StringComparison.Ordinal ),
			"The creative document must not serialize generated filenames as resource dependencies." );
		var wrapper = ModelDocWriter.WriteSourceWrapper( "weapon.fbx", "root" );
		Check(
			report,
			wrapper.Contains( "original_bone_name = \"root\"" )
				&& wrapper.Contains( "new_bone_name = \"weapon_root\"" ),
			"Source wrappers must normalize the selected weapon root." );
		var host = ModelDocWriter.WriteHost(
			"host_reference.dmx",
			[],
			"weapon.vanmgrph",
			skeleton.Bones.Select( bone => bone.Name ),
			new HostWeaponMesh(
				"weapon.fbx",
				"root",
				new Transform( Vector3.Zero, Rotation.Identity, Vector3.One * 0.6f ),
				[],
				[
					new HostMaterialRemap(
						"frame.vmat",
						"weapons/test/materials/frame.vmat" )
				] ),
			[
				new HostAttachment(
					"muzzle",
					"weapon_root",
					Vector3.Forward * 10,
					Rotation.Identity )
			] );
		Check(
			report,
			host.Contains( "target_bone = \"weapon_root\"", StringComparison.Ordinal )
				&& host.Contains( "do_not_discard = true", StringComparison.Ordinal )
				&& host.Contains( "filename = \"weapon.fbx\"", StringComparison.Ordinal )
				&& host.Contains( "import_scale = 0.6", StringComparison.Ordinal )
				&& host.Contains( "anim_graph_name = \"weapon.vanmgrph\"", StringComparison.Ordinal )
				&& host.Contains( "use_global_default = false", StringComparison.Ordinal )
				&& !host.Contains( "use_global_default = true", StringComparison.Ordinal )
				&& host.Contains( "from = \"frame.vmat\"", StringComparison.Ordinal )
				&& host.Contains( "from = \"materials/tools/toolsinvisible.vmat\"", StringComparison.Ordinal )
				&& host.Contains( "_class = \"Attachment\"", StringComparison.Ordinal )
				&& host.Contains( "name = \"muzzle\"", StringComparison.Ordinal ),
			"Host ModelDocs must preserve generated bones, safely handle imported materials, and contain the visible weapon, graph, and attachments." );
		var skeletonOnlyHost = ModelDocWriter.WriteHost(
			"host_reference.dmx",
			[],
			"",
			skeleton.Bones.Select( bone => bone.Name ) );
		Check(
			report,
			skeletonOnlyHost.Contains( "use_global_default = false", StringComparison.Ordinal ),
			"A skeleton-only host must retain the invisible carrier material without substitution." );
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

	private static void TestGenerationSourceAdapters( WeaponAnimatorSelfTestReport report )
	{
		var source = $$"""
			{{ModelDocWriter.Header}}
			{
				rootNode =
				{
					_class = "RootNode"
					children =
					[
						{
							_class = "RenderMeshFile"
							filename = "receiver.fbx"
							import_translation = [ 2, 0, 0 ]
							import_rotation = [ 0, 0, 0 ]
							import_scale = 1
						},
						{
							_class = "RenderMeshFile"
							filename = "magazine.fbx"
							import_translation = [ 0, 2, 0 ]
							import_rotation = [ 0, 0, 0 ]
							import_scale = 1
						},
					]
				}
			}
			""";
		var adapted = ModelDocWriter.WriteVmdlSourceAdapter(
			source,
			"root",
			["foreign_arm"],
			new Transform( new Vector3( 10, 0, 0 ), Rotation.Identity, 0.5f ) );
		Check(
			report,
			Count( adapted, "import_scale = 0.5" ) == 2
				&& adapted.Contains( "import_translation = [ 11, 0, 0 ]", StringComparison.Ordinal )
				&& adapted.Contains( "import_translation = [ 10, 1, 0 ]", StringComparison.Ordinal )
				&& adapted.Contains( "original_bone_name = \"root\"", StringComparison.Ordinal )
				&& adapted.Contains( "\"foreign_arm\"", StringComparison.Ordinal ),
			"A VMDL adapter must apply calibration to every render mesh while preserving filtering." );

		var baseHost = ModelDocWriter.WriteHost(
			"reference.dmx",
			[],
			"",
			["weapon_root"],
			baseModelPath: "weapons/test/source_adapter.vmdl" );
		Check(
			report,
			baseHost.Contains(
				"base_model_name = \"weapons/test/source_adapter.vmdl\"",
				StringComparison.Ordinal ),
			"Generated hosts must be able to derive their visible mesh from a VMDL adapter." );

		var temporary = Path.Combine(
			Path.GetTempPath(),
			$"weaponanim-source-{Guid.NewGuid():N}.vmdl" );
		File.WriteAllText( temporary, source );
		try
		{
			var document = ValidDocument();
			document.Source.SourcePath = temporary;
			document.Source.CompiledModelPath = temporary;
			document.Calibration.PhysicalTransform =
				new Transform( Vector3.Zero, Rotation.Identity, 0.6f );
			var progress = new List<GenerationProgress>();
			var generated = AssetGenerationService.BuildFiles(
				document,
				HostSkeletonBuilder.Build( document, includeArmProfile: false ),
				"weapons/test_weapon/viewmodel",
				progress.Add );
			Check(
				report,
				generated.ContainsKey( "test_weapon_source_adapter.vmdl" )
					&& generated["test_weapon_vm.vmdl"].Contains(
						"base_model_name = \"weapons/test_weapon/viewmodel/test_weapon_source_adapter.vmdl\"",
						StringComparison.Ordinal ),
				"VMDL source projects must generate a persistent calibrated adapter." );
			Check(
				report,
				progress.Any( item =>
					item.Stage == "Sequences"
					&& item.Completed == 1
					&& item.Total == 1 ),
				"Generation must report deterministic per-sequence progress." );
			using var cancellation = new System.Threading.CancellationTokenSource();
			cancellation.Cancel();
			var cancelled = false;
			try
			{
				AssetGenerationService.BuildFiles(
					document,
					HostSkeletonBuilder.Build( document, includeArmProfile: false ),
					"weapons/test_weapon/viewmodel",
					cancellationToken: cancellation.Token );
			}
			catch ( OperationCanceledException )
			{
				cancelled = true;
			}
			Check(
				report,
				cancelled,
				"Generation must honor cancellation before assembling or replacing output files." );
		}
		finally
		{
			File.Delete( temporary );
		}
	}

	private static string DmxJointIdForTest( int index )
	{
		var bytes = System.Security.Cryptography.SHA256.HashData(
			System.Text.Encoding.UTF8.GetBytes( $"SboxWeaponAnimator.DmxReference:joint:{index}" ) );
		return new Guid( bytes.AsSpan( 0, 16 ) ).ToString();
	}

	private static string DmxAnimationJointIdForTest(
		WeaponAnimationClip clip,
		int index )
	{
		var key =
			$"SboxWeaponAnimator.DmxReference:animation:{clip.Id}:joint:{index}";
		var bytes = System.Security.Cryptography.SHA256.HashData(
			System.Text.Encoding.UTF8.GetBytes( key ) );
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
					[(idle, "idle.dmx")],
					"weapon.vanmgrph",
					["root", "weapon_root"] );
			var prefabFrench = PrefabWriter.Write( document, "host.vmdl" );

			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo( "en-US" );
			Equal( report, graphFrench, AnimGraphWriter.Write( document, "weapons/test/host.vmdl" ), "AnimGraph output must be culture-independent." );
			Equal(
					report,
					modelFrench,
					ModelDocWriter.WriteHost(
						"host_reference.dmx",
						[(idle, "idle.dmx")],
						"weapon.vanmgrph",
						["root", "weapon_root"] ),
				"ModelDoc output must be culture-independent." );
			Equal( report, prefabFrench, PrefabWriter.Write( document, "host.vmdl" ), "Prefab output must be culture-independent." );
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

		var skeleton = HostSkeletonBuilder.Build( document, includeArmProfile: false );
		var generated = AssetGenerationService.BuildFiles(
			document,
			skeleton,
			"weapons/test_weapon/viewmodel" );
		var finalHost = generated["test_weapon_vm.vmdl"];
		var bootstrapHost = generated["test_weapon_vm_bootstrap.vmdl"];
		var generatedGraph = generated["test_weapon.vanmgrph"];
		Check(
			report,
			finalHost.Contains(
				"anim_graph_name = \"weapons/test_weapon/viewmodel/test_weapon.vanmgrph\"",
				StringComparison.Ordinal )
				&& bootstrapHost.Contains( "anim_graph_name = \"\"", StringComparison.Ordinal )
				&& generatedGraph.Contains(
					"m_previewModels = [ \"weapons/test_weapon/viewmodel/test_weapon_vm_bootstrap.vmdl\", ]",
					StringComparison.Ordinal ),
			"Generation must keep a permanent graph-free preview host while the final host always links its AnimGraph." );
		Check(
			report,
			generated.ContainsKey( "test_weapon_sequence_idle.dmx" )
				&& !generated.ContainsKey( "test_weapon_idle.dmx" )
				&& !generated.ContainsKey( "test_weapon_sequence_fire.dmx" ),
			"Generation must emit authored sequences only and leave missing action roles on Idle fallbacks." );

		var custom = WeaponAnimationClip.Create( WeaponClipRole.Custom );
		custom.Name = "Mechanical Check";
		custom.Readiness = ClipReadiness.Draft;
		document.Clips.Add( custom );
		WeaponAnimationNames.RepairCustomSequenceNames( document );
		generated = AssetGenerationService.BuildFiles(
			document,
			skeleton,
			"weapons/test_weapon/viewmodel" );
		Check(
			report,
			generated.ContainsKey(
				$"test_weapon_sequence_{custom.GeneratedSequenceName}.dmx" ),
			"Authored custom clips must use their persisted readable sequence name." );
	}

	private static void TestPartVisibility( WeaponAnimatorSelfTestReport report )
	{
		var document = ValidDocument();
		var idle = document.EnsureClip( WeaponClipRole.Idle );
		var part = new WeaponVisibilityPart
		{
			Name = "Spare Magazine",
			BoneId = "weapon_root",
			BoneName = "weapon_root",
			DefaultVisible = false
		};
		document.Rig.VisibilityParts.Add( part );
		var track = idle.EnsureVisibilityTrack( part.Id );
		var show = WeaponVisibilityEvaluator.UpsertKey( track, 0.2f, true );
		WeaponVisibilityEvaluator.UpsertKey( track, 0.8f, false );

		Check(
			report,
			!WeaponVisibilityEvaluator.Evaluate( part, idle, 0.1f )
				&& WeaponVisibilityEvaluator.Evaluate( part, idle, 0.5f )
				&& !WeaponVisibilityEvaluator.Evaluate( part, idle, 0.9f ),
			"Visibility tracks must evaluate as stepped state changes from the configured default." );
		var replacement = WeaponVisibilityEvaluator.UpsertKey( track, 0.2f, false );
		Equal(
			report,
			show.Id,
			replacement.Id,
			"Keying visibility twice at one frame must update the existing key." );
		Check(
			report,
			!WeaponVisibilityEvaluator.Evaluate( part, idle, 0.5f ),
			"A replaced visibility key must take effect immediately." );
		replacement.Visible = true;

		var spans = WeaponVisibilityEvaluator.BuildSpans( part, idle );
		Equal( report, 3, spans.Count, "Visibility export must cover the full clip with deterministic state spans." );
		Near( report, 0, spans[0].StartTime, 0.0001f, "The first visibility span must begin at clip start." );
		Near( report, idle.Duration, spans[^1].EndTime, 0.0001f, "The final visibility span must reach clip end." );

		var skeleton = HostSkeletonBuilder.Build( document, includeArmProfile: false );
		var before = DmxWriter.WriteAnimation( document, skeleton, idle );
		var graph = AnimGraphWriter.Write( document, "host.vmdl" );
		var after = DmxWriter.WriteAnimation( document, skeleton, idle );
		Equal(
			report,
			before,
			after,
			"Visibility export must be deterministic and must not mutate authored transforms." );
		Check(
			report,
			before.Contains( "\"DmeFloatLogLayer\"", StringComparison.Ordinal )
				&& before.Contains( "\"0.0001\"", StringComparison.Ordinal )
				&& before.Contains( "-8192", StringComparison.Ordinal ),
			"Bone visibility must use native sequence scale and off-screen position channels." );
		Check(
			report,
			graph.Contains( WeaponVisibilityEvaluator.VisibleTag( part.Id ) )
				&& graph.Contains( WeaponVisibilityEvaluator.HiddenTag( part.Id ) ),
			"Generated AnimGraphs must declare both visibility states for every part." );

		var prefab = PrefabWriter.Write( document, "host.vmdl" );
		Check(
			report,
			!prefab.Contains( "WeaponPartVisibilityController", StringComparison.Ordinal )
				&& !prefab.Contains( "\"Name\": \"source_weapon\"", StringComparison.Ordinal )
				&& prefab.Contains( "\"Model\": \"host.vmdl\"", StringComparison.Ordinal )
				&& prefab.Contains( "\"GameLayer\": true", StringComparison.Ordinal )
				&& Count( prefab, "\"__type\": \"Sandbox.SkinnedModelRenderer\"" ) == 2
				&& prefab.Contains( "\"Name\": \"muzzle\"", StringComparison.Ordinal )
				&& prefab.Contains( "\"Name\": \"eject\"", StringComparison.Ordinal ),
			"Generated prefabs must use one visible host renderer plus bone-merged arms and explicit output anchors, with no custom controller." );
		document.Output.GenerateGraph = false;
		var graphFreePrefab = PrefabWriter.Write( document, "host.vmdl" );
		Check(
			report,
			graphFreePrefab.Contains( "\"UseAnimGraph\": false", StringComparison.Ordinal )
				&& !graphFreePrefab.Contains( "WeaponPartVisibilityController", StringComparison.Ordinal ),
			"Graph-free prefabs must remain standard and disable AnimGraph playback." );
		document.Output.GenerateGraph = true;

		var controller = new WeaponAnimatorController();
		controller.SetDocument( document );
		controller.SetTimelineTime( 0.2f );
		controller.SelectKeys( [show.Id], false );
		controller.CopySelectedKeys();
		controller.SetTimelineTime( 0.5f );
		controller.PasteKeys();
		Check(
			report,
			idle.VisibilityTracks.Single( x => x.PartId == part.Id )
				.Keys.Any( x => MathF.Abs( x.Time - 0.5f ) <= 0.0001f ),
			"Visibility keys must participate in the shared copy and paste workflow." );

		part.RenderMode = VisibilityRenderMode.BodyGroup;
		part.BodyGroupName = "";
		var invalid = WeaponAnimationValidator.ValidateForGeneration( document );
		Check(
			report,
			invalid.Issues.Any( x => x.Code == "visibility.bodygroup_missing" )
				&& invalid.Issues.Any( x => x.Code == "visibility.bodygroup_export" ),
			"Generation validation must reject bodygroup visibility until it can be baked into a standard prefab." );
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
		document.Calibration.SetAnchor( Anchor( AnchorKind.Muzzle, new Vector3( 12, 0, 1 ) ) );
		document.Calibration.SetAnchor( Anchor( AnchorKind.Eject, new Vector3( 4, -1, 2 ) ) );
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
