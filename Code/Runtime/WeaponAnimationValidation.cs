#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;

namespace SboxWeaponAnimator;

public enum ValidationSeverity
{
	Info,
	Warning,
	Error
}

public sealed class ValidationIssue
{
	public ValidationSeverity Severity { get; init; }
	public string Code { get; init; } = "";
	public string Message { get; init; } = "";
	public string Context { get; init; } = "";
	public bool Blocking => Severity == ValidationSeverity.Error;
}

public sealed class ValidationReport
{
	public List<ValidationIssue> Issues { get; } = [];
	public bool IsValid => Issues.All( x => !x.Blocking );
	public int ErrorCount => Issues.Count( x => x.Severity == ValidationSeverity.Error );
	public int WarningCount => Issues.Count( x => x.Severity == ValidationSeverity.Warning );

	public void Add( ValidationSeverity severity, string code, string message, string context = "" )
	{
		Issues.Add( new ValidationIssue
		{
			Severity = severity,
			Code = code,
			Message = message,
			Context = context
		} );
	}
}

public static class WeaponAnimationValidator
{
	private static readonly HashSet<string> ReservedArmBones = new( StringComparer.OrdinalIgnoreCase )
	{
		"root",
		"camera",
		"weapon_root",
		"hold_R",
		"hold_L",
		"hand_R",
		"hand_L",
		"wrist_R",
		"wrist_L",
		"elbow_R",
		"elbow_L",
		"arm_upper_R",
		"arm_upper_L",
		"clavicle_R",
		"clavicle_L"
	};

	public static ValidationReport ValidateCalibration( WeaponAnimationDocument document )
	{
		var report = new ValidationReport();

		if ( string.IsNullOrWhiteSpace( document.Source.SourcePath ) )
			report.Add( ValidationSeverity.Error, "source.missing", "Choose a rigged source model." );
		else if ( !document.Source.Compiled )
			report.Add( ValidationSeverity.Error, "source.not_compiled", "The source model has not compiled successfully." );

		if ( document.Source.Compiled && !document.Source.PreviewHostCompiled )
			report.Add( ValidationSeverity.Error, "host.not_compiled", "The generated preview animation host has not compiled and reloaded." );

		if ( document.Rig.Bones.Count == 0 )
			report.Add( ValidationSeverity.Error, "rig.empty", "The source model has no readable skeleton." );

		var roots = document.Rig.Bones.Count( x => x.Classification == WeaponBoneClassification.WeaponRoot );
		if ( roots != 1 || string.IsNullOrWhiteSpace( document.Rig.RootBone ) )
			report.Add( ValidationSeverity.Error, "rig.root", "Classify exactly one weapon root." );
		if ( document.Rig.ReviewRequired || !document.Rig.FilteredPreviewConfirmed )
		{
			report.Add(
				ValidationSeverity.Error,
				"rig.review_required",
				"Review the weapon subtree and confirm that the filtered preview contains only weapon bones." );
		}

		if ( !WeaponAnimationMath.IsFinite( document.Calibration.UniformScale )
			|| document.Calibration.UniformScale <= 0 )
		{
			report.Add( ValidationSeverity.Error, "scale.invalid", "Uniform scale must be finite and greater than zero." );
		}
		else
		{
			var dimensions = document.Calibration.Measurement.ResultingDimensions;
			var longest = MathF.Max( dimensions.x, MathF.Max( dimensions.y, dimensions.z ) );
			if ( longest > 0 && (longest < 2.0f || longest > 80.0f) )
			{
				report.Add(
					ValidationSeverity.Warning,
					"scale.implausible",
					$"The calibrated weapon is {longest:0.##} inches on its longest axis, which is unusual relative to the reference arms." );
			}
		}

		RequireAnchor( document, report, AnchorKind.Grip, "Choose a primary-hand grip anchor." );

		if ( document.Rig.AuditIssues.Any( x => x.Severity == ValidationSeverity.Error ) )
			report.Add( ValidationSeverity.Error, "rig.conflicts", "Resolve the blocking skeleton audit issues." );

		if ( document.Source.Compiled
			&& !string.IsNullOrWhiteSpace( document.Rig.RootBone )
			&& document.Rig.Bones.Count > 0 )
		{
			var duplicateNames = document.Rig.Bones
				.GroupBy( x => x.Name, StringComparer.OrdinalIgnoreCase )
				.Where( x => x.Count() > 1 )
				.Select( x => x.Key );

			foreach ( var duplicate in duplicateNames )
				report.Add( ValidationSeverity.Error, "rig.duplicate", $"Duplicate bone '{duplicate}'.", duplicate );

				foreach ( var bone in document.Rig.RetainedBones() )
				{
					if ( ReservedArmBones.Contains( bone.Name )
						&& !bone.Id.Equals(
							document.Rig.SourceSkeletonRootId,
							StringComparison.OrdinalIgnoreCase ) )
					{
					report.Add(
						ValidationSeverity.Error,
						"rig.reserved_name",
						$"Weapon bone '{bone.Name}' conflicts with the Facepunch arm skeleton.",
						bone.Name );
				}
			}
		}

		return report;
	}

	public static ValidationReport ValidateForGeneration( WeaponAnimationDocument document )
	{
		var report = ValidateCalibration( document );
		ValidateMaterials( document, report );
		ValidateVisibilityParts( document, report );

		if ( !document.Calibration.Confirmed || document.Calibration.Snapshot is null )
			report.Add( ValidationSeverity.Error, "calibration.unconfirmed", "Confirm calibration before generating." );

		var idle = document.Clips.FirstOrDefault( x => x.Role == WeaponClipRole.Idle );
		if ( idle is null || idle.Readiness is ClipReadiness.NotStarted or ClipReadiness.Warning )
			report.Add( ValidationSeverity.Error, "clip.idle", "Idle must be authored and marked Draft or Ready." );

		foreach ( var clip in document.Clips )
		{
			if ( clip.Duration <= 0 || !WeaponAnimationMath.IsFinite( clip.Duration ) )
				report.Add( ValidationSeverity.Error, "clip.duration", $"{clip.Name} has an invalid duration.", clip.Name );

			if ( clip.SampleRate <= 0 || !WeaponAnimationMath.IsFinite( clip.SampleRate ) )
				report.Add( ValidationSeverity.Error, "clip.sample_rate", $"{clip.Name} has an invalid sample rate.", clip.Name );

			foreach ( var track in clip.Tracks )
			{
				foreach ( var key in track.Keys )
				{
					if ( key.Time < 0 || key.Time > clip.Duration + 0.0001f )
						report.Add(
							ValidationSeverity.Warning,
							"key.outside_clip",
							$"{clip.Name}/{track.Target} has a key outside the clip duration.",
							track.Target );
				}

				var keyIds = track.Keys.Select( x => x.Id ).ToHashSet();
				foreach ( var span in track.CurveSpans )
				{
					if ( !keyIds.Contains( span.StartKeyId ) || !keyIds.Contains( span.EndKeyId ) )
					{
						report.Add(
							ValidationSeverity.Warning,
							"curve.span_orphan",
							$"{clip.Name}/{track.Target} contains curve data for missing keys; it will be ignored.",
							track.Target );
						continue;
					}

					if ( span.HasSpeedCurve
						&& WeaponAnimationMath.MotionRateArea( span.Speed ) <= 0.0001f )
					{
						report.Add(
							ValidationSeverity.Warning,
							"curve.speed_invalid",
							$"{clip.Name}/{track.Target} has a zero-area speed curve and will use linear timing.",
							track.Target );
					}
				}
			}

			foreach ( var track in clip.VisibilityTracks )
			{
				if ( document.Rig.VisibilityParts.All( x => x.Id != track.PartId ) )
				{
					report.Add(
						ValidationSeverity.Error,
						"visibility.part_missing",
						$"{clip.Name} contains a visibility track for a missing part.",
						clip.Name );
				}

				foreach ( var key in track.Keys )
				{
					if ( key.Time < 0 || key.Time > clip.Duration + 0.0001f )
					{
						report.Add(
							ValidationSeverity.Warning,
							"visibility.key_outside_clip",
							$"{clip.Name} has a visibility key outside the clip duration.",
							clip.Name );
					}
				}
			}
		}

		foreach ( var role in WeaponAnimationDocument.StandardClips() )
		{
			if ( role == WeaponClipRole.Idle )
				continue;

			var clip = document.Clips.FirstOrDefault( x => x.Role == role );
			if ( clip is null || clip.Readiness == ClipReadiness.NotStarted )
			{
				report.Add(
					ValidationSeverity.Warning,
					"clip.fallback",
					$"{WeaponAnimationNames.DisplayName( role )} will use the validated Idle fallback." );
			}
		}

		if ( document.Graph.ReloadProfile == ReloadProfile.Incremental )
		{
			var required = new[]
			{
				WeaponClipRole.ReloadEnter,
				WeaponClipRole.FirstShell,
				WeaponClipRole.InsertShell,
				WeaponClipRole.ReloadExit
			};

			foreach ( var role in required )
			{
				var clip = document.Clips.FirstOrDefault( x => x.Role == role );
				if ( clip is null || clip.Readiness == ClipReadiness.NotStarted )
					report.Add(
						ValidationSeverity.Warning,
						"reload.incremental_fallback",
						$"{WeaponAnimationNames.DisplayName( role )} is missing; incremental reload will fall back to Idle." );
			}
		}

		return report;
	}

	private static void ValidateMaterials(
		WeaponAnimationDocument document,
		ValidationReport report )
	{
		if ( document.Source.Materials.Count == 0
			&& document.Source.NeedsModelDocWrapper )
		{
			report.Add(
				ValidationSeverity.Warning,
				"material.none",
				"No imported material slots are recorded. Reimport the source to discover nearby textures." );
			return;
		}

		foreach ( var duplicate in document.Source.Materials
			.Where( material => !string.IsNullOrWhiteSpace( material.SourceMaterialPath ) )
			.GroupBy(
				material => material.SourceMaterialPath,
				StringComparer.OrdinalIgnoreCase )
			.Where( group => group.Count() > 1 ) )
		{
			report.Add(
				ValidationSeverity.Error,
				"material.duplicate_slot",
				$"Source material slot '{duplicate.Key}' is mapped more than once.",
				duplicate.Key );
		}

		foreach ( var duplicate in document.Source.Materials
			.Where( material => !string.IsNullOrWhiteSpace( material.OutputName ) )
			.GroupBy( material => material.OutputName, StringComparer.OrdinalIgnoreCase )
			.Where( group => group.Count() > 1 ) )
		{
			report.Add(
				ValidationSeverity.Error,
				"material.duplicate_output",
				$"Generated material name '{duplicate.Key}' is not unique.",
				duplicate.Key );
		}

		foreach ( var material in document.Source.Materials )
		{
			if ( string.IsNullOrWhiteSpace( material.SourceMaterialPath ) )
			{
				report.Add(
					ValidationSeverity.Error,
					"material.slot_missing",
					$"Material '{material.Name}' has no source slot name.",
					material.Name );
			}
			if ( material.FindTexture( WeaponTextureChannel.PackedOrm ) is not null
				&& material.FindTexture( WeaponTextureChannel.Roughness ) is null
				&& material.FindTexture( WeaponTextureChannel.Metalness ) is null )
			{
				report.Add(
					ValidationSeverity.Warning,
					"material.packed_orm",
					$"{material.Name} uses a packed ORM map that cannot be assigned automatically.",
					material.Name );
			}
		}
	}

	private static void ValidateVisibilityParts(
		WeaponAnimationDocument document,
		ValidationReport report )
	{
		foreach ( var duplicate in document.Rig.VisibilityParts
			.GroupBy( x => x.Id )
			.Where( x => x.Count() > 1 ) )
		{
			report.Add(
				ValidationSeverity.Error,
				"visibility.duplicate_id",
				$"Visibility part ID '{duplicate.Key}' is duplicated." );
		}

		foreach ( var duplicate in document.Rig.VisibilityParts
			.Where( x => !string.IsNullOrWhiteSpace( x.Name ) )
			.GroupBy( x => x.Name, StringComparer.OrdinalIgnoreCase )
			.Where( x => x.Count() > 1 ) )
		{
			report.Add(
				ValidationSeverity.Warning,
				"visibility.duplicate_name",
				$"Multiple visibility parts are named '{duplicate.Key}'." );
		}

		foreach ( var part in document.Rig.VisibilityParts )
		{
			if ( string.IsNullOrWhiteSpace( part.Name ) )
				report.Add(
					ValidationSeverity.Error,
					"visibility.name_missing",
					"A visibility part needs a display name." );

			if ( part.RenderMode == VisibilityRenderMode.BodyGroup )
			{
				report.Add(
					ValidationSeverity.Error,
					"visibility.bodygroup_export",
					$"{part.Name} uses bodygroup visibility, which cannot be baked into a standard generated viewmodel yet.",
					part.Name );
				if ( string.IsNullOrWhiteSpace( part.BodyGroupName ) )
					report.Add(
						ValidationSeverity.Error,
						"visibility.bodygroup_missing",
						$"{part.Name} needs a bodygroup name.",
						part.Name );
				if ( part.VisibleBodyGroupValue == part.HiddenBodyGroupValue )
					report.Add(
						ValidationSeverity.Error,
						"visibility.bodygroup_values",
						$"{part.Name}'s visible and hidden bodygroup values must differ.",
						part.Name );
				continue;
			}

			var bone = document.Rig.RetainedBones().FirstOrDefault( x =>
				x.Id.Equals( part.BoneId, StringComparison.OrdinalIgnoreCase )
				|| x.Name.Equals( part.BoneName, StringComparison.OrdinalIgnoreCase ) );
			if ( bone is null )
			{
				report.Add(
					ValidationSeverity.Error,
					"visibility.bone_missing",
					$"{part.Name} references a missing or excluded weapon bone.",
					part.BoneName );
			}
		}
	}

	private static void RequireAnchor(
		WeaponAnimationDocument document,
		ValidationReport report,
		AnchorKind kind,
		string message )
	{
		var anchor = document.Calibration.GetAnchor( kind );
		if ( anchor is null || !WeaponAnimationMath.IsFinite( anchor.LocalPosition ) )
			report.Add( ValidationSeverity.Error, $"anchor.{kind.ToString().ToLowerInvariant()}", message );
	}
}
