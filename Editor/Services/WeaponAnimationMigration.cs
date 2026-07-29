#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public sealed class WeaponAnimationMigrationResult
{
	public bool Migrated { get; init; }
	public bool CurveSchemaMigrated { get; init; }
	public bool RepairedLegacyIdle { get; init; }
	public bool RepairedMaterialMetadata { get; init; }
	public bool RepairedSequenceNames { get; init; }
	public bool RepairedSourceDimensions { get; init; }
	public bool RepairedKeyOrder { get; init; }
	public int SourceSchemaVersion { get; init; }
	public int PreservedWeaponTracks { get; init; }
	public int RemovedTracks { get; init; }
	public int RemovedConstraints { get; init; }
	public bool Changed =>
		Migrated
		|| RepairedLegacyIdle
		|| RepairedMaterialMetadata
		|| RepairedSequenceNames
		|| RepairedSourceDimensions
		|| RepairedKeyOrder;

	public string Summary
	{
		get
		{
			if ( Migrated )
			{
				if ( CurveSchemaMigrated && SourceSchemaVersion >= 3 )
				{
					return $"Migrated schema {SourceSchemaVersion} → {WeaponAnimationDocument.CurrentSchemaVersion}. "
						+ "Existing interpolation was preserved; editable curve data will be created only when changed.";
				}

				return $"Migrated schema {SourceSchemaVersion} → {WeaponAnimationDocument.CurrentSchemaVersion}. "
					+ $"Preserved {PreservedWeaponTracks} weapon tracks; reset {RemovedTracks} arm or incompatible tracks"
					+ (RemovedConstraints > 0 ? $" and {RemovedConstraints} constraints." : ".");
			}

			if ( RepairedLegacyIdle )
			{
				return "Repaired a legacy Idle bind pose that stored weapon bones in model space. "
					+ "The original version will be backed up when this project is saved.";
			}
			if ( RepairedSequenceNames )
			{
				return "Assigned stable, readable sequence names to custom clips. "
					+ "The original version will be backed up when saved.";
			}
			if ( RepairedSourceDimensions )
			{
				return "Recovered source-model dimensions for calibration validation. "
					+ "The original version will be backed up when saved.";
			}
			if ( RepairedKeyOrder )
			{
				return "Normalized animation key order for efficient playback. "
					+ "The original version will be backed up when saved.";
			}
			return RepairedMaterialMetadata
				? "Repaired imported material slot metadata so source labels are not treated "
					+ "as asset dependencies. The original version will be backed up when saved."
				: "";
		}
	}
}

public static class WeaponAnimationMigration
{
	public static WeaponAnimationMigrationResult MigrateAndRepair(
		WeaponAnimationDocument document )
	{
		document.Source ??= new SourceModelSettings();
		document.Source.Materials ??= [];

		if ( document.SchemaVersion > WeaponAnimationDocument.CurrentSchemaVersion )
		{
			throw new InvalidOperationException(
				$"This .wepanim uses schema {document.SchemaVersion}, newer than supported schema "
				+ $"{WeaponAnimationDocument.CurrentSchemaVersion}." );
		}

		var sourceVersion = document.SchemaVersion;
		var migrated = sourceVersion < WeaponAnimationDocument.CurrentSchemaVersion;
		var separatedRigMigration = sourceVersion < 3;
		var curveSchemaMigration = sourceVersion < 4;
		var preservedTracks = 0;
		var removedTracks = 0;
		var removedConstraints = 0;
		var repairedMaterialMetadata = false;
		var repairedSourceDimensions = false;
		var repairedKeyOrder = false;
		foreach ( var material in document.Source.Materials )
		{
			var storedSlot = WeaponMaterialPipeline.StoredMaterialSlot(
				material.SourceMaterialPath );
			if ( !storedSlot.Equals(
				material.SourceMaterialPath,
				StringComparison.Ordinal ) )
			{
				material.SourceMaterialPath = storedSlot;
				repairedMaterialMetadata = true;
			}
		}
		if ( document.Source.OriginalModelDimensions.Length <= 0
			&& !string.IsNullOrWhiteSpace( document.Source.CompiledModelPath ) )
		{
			try
			{
				var sourceModel = Model.Load( document.Source.CompiledModelPath );
				if ( sourceModel is not null
					&& !sourceModel.IsError
					&& sourceModel.Bounds.Size.Length > 0 )
				{
					document.Source.OriginalModelDimensions = sourceModel.Bounds.Size;
					repairedSourceDimensions = true;
				}
			}
			catch
			{
				// The preview recovery path can populate this after the missing asset returns.
			}
		}

		if ( separatedRigMigration )
		{
			WeaponRigHierarchy.RepairMetadata( document.Rig, true );
			foreach ( var bone in document.Rig.Bones )
			{
				bone.Inclusion = bone.Classification == WeaponBoneClassification.Ignored
					? WeaponBoneInclusion.Excluded
					: WeaponBoneInclusion.Included;
			}

			var root = document.Rig.Bones.FirstOrDefault( x =>
				x.Classification == WeaponBoneClassification.WeaponRoot )
				?? document.Rig.FindBone( document.Rig.RootBone )
				?? document.Rig.Bones.FirstOrDefault();
			if ( root is not null )
			{
				root.Classification = WeaponBoneClassification.WeaponRoot;
				root.Inclusion = WeaponBoneInclusion.Included;
				document.Rig.RootBone = root.Name;
				document.Rig.WeaponSubtreeRootId = root.Id;
			}

			var retainedNames = document.Rig.RetainedBones()
				.Select( x => x.Name )
				.ToHashSet( StringComparer.OrdinalIgnoreCase );
			retainedNames.Add( "weapon_root" );
			foreach ( var clip in document.Clips )
			{
				foreach ( var track in clip.Tracks.ToArray() )
				{
					var isOldRoot = root is not null
						&& track.Target.Equals( root.Name, StringComparison.OrdinalIgnoreCase );
					var preserve = track.Kind == RigControlKind.Weapon
						&& (isOldRoot || retainedNames.Contains( track.Target ));
					if ( !preserve )
					{
						clip.Tracks.Remove( track );
						removedTracks++;
						continue;
					}

					if ( isOldRoot )
						track.Target = "weapon_root";
					preservedTracks++;
				}

				removedConstraints += clip.Constraints.RemoveAll( constraint =>
					!retainedNames.Contains( constraint.TargetBone ) );
			}

			var configuration = document.Binding.Configuration;
			document.Binding = new ArmBindingDefinition
			{
				Configuration = configuration
			};
			document.Calibration.Confirmed = false;
			document.ActiveStage = WeaponAnimatorStage.Calibrate;
			document.Source.PreviewHostCompiled = false;
			document.Rig.ReviewRequired = true;
			document.Rig.FilteredPreviewConfirmed = false;
			document.Rig.ProfileHash = WeaponSourceImporter.HashText(
				WeaponRigHierarchy.ProfileText( document.Rig ) );
		}
		else
		{
			WeaponRigHierarchy.RepairMetadata( document.Rig, false );
		}

		foreach ( var track in document.Clips.SelectMany( x => x.Tracks ) )
		{
			repairedKeyOrder |= !KeysAreSorted( track.Keys.Select( key => key.Time ) );
			track.Keys.Sort( ( left, right ) => left.Time.CompareTo( right.Time ) );
			foreach ( var key in track.Keys )
			{
				key.CurveTangents ??= new TransformCurveTangents();
				if ( curveSchemaMigration )
				{
					key.CurveTangents.PositionIn = key.InTangent;
					key.CurveTangents.PositionOut = key.OutTangent;
				}
			}
			WeaponAnimationMath.RepairCurveSpans( track );
		}
		foreach ( var track in document.Clips.SelectMany( x => x.VisibilityTracks ) )
		{
			repairedKeyOrder |= !KeysAreSorted( track.Keys.Select( key => key.Time ) );
			track.Keys.Sort( ( left, right ) => left.Time.CompareTo( right.Time ) );
		}
		var repairedSequenceNames =
			WeaponAnimationNames.RepairCustomSequenceNames( document )
			| WeaponAnimationNames.RepairCustomAnchorNames( document );

		var repairedLegacyIdle = !separatedRigMigration && RepairLegacyIdleBindPose( document );
		document.SchemaVersion = WeaponAnimationDocument.CurrentSchemaVersion;
		foreach ( var role in WeaponAnimationDocument.StandardClips() )
			document.EnsureClip( role );
		if ( document.Workspace.SelectedClipId == Guid.Empty
			|| document.Clips.All( x => x.Id != document.Workspace.SelectedClipId ) )
			document.Workspace.SelectedClipId = document.EnsureClip( WeaponClipRole.Idle ).Id;
		if ( string.IsNullOrWhiteSpace( document.Output.AssetName ) )
			document.Output.AssetName = WeaponAnimationDocument.Slugify( document.Name );

		return new WeaponAnimationMigrationResult
		{
			Migrated = migrated,
			CurveSchemaMigrated = curveSchemaMigration,
			RepairedLegacyIdle = repairedLegacyIdle,
			RepairedMaterialMetadata = repairedMaterialMetadata,
			RepairedSequenceNames = repairedSequenceNames,
			RepairedSourceDimensions = repairedSourceDimensions,
			RepairedKeyOrder = repairedKeyOrder,
			SourceSchemaVersion = sourceVersion,
			PreservedWeaponTracks = preservedTracks,
			RemovedTracks = removedTracks,
			RemovedConstraints = removedConstraints
		};
	}

	private static bool KeysAreSorted( IEnumerable<float> times )
	{
		var hasPrevious = false;
		var previous = 0.0f;
		foreach ( var time in times )
		{
			if ( hasPrevious && previous > time )
				return false;
			previous = time;
			hasPrevious = true;
		}
		return true;
	}

	internal static bool RepairLegacyIdleBindPose(
		WeaponAnimationDocument document,
		HostSkeleton? authoritativeSkeleton = null )
	{
		var idle = document.Clips.FirstOrDefault( x => x.Role == WeaponClipRole.Idle );
		var retained = document.Rig.RetainedBones().ToList();
		if ( idle is null || retained.Count < 2
			|| idle.Constraints.Count > 0
			|| idle.Tags.Count > 0
			|| idle.VisibilityTracks.Any( x => x.Keys.Count > 0 )
			|| idle.ParameterEvents.Count > 0 )
			return false;

		var skeleton = authoritativeSkeleton
			?? HostSkeletonBuilder.Build( document, includeArmProfile: false );
		var weaponBones = skeleton.Bones.Where( x => x.IsWeaponBone ).ToList();
		if ( weaponBones.Count != retained.Count )
			return false;

		var tracks = weaponBones
			.Select( bone => idle.Tracks.FirstOrDefault( track =>
				track.Kind == RigControlKind.Weapon
				&& track.Target.Equals( bone.Name, StringComparison.OrdinalIgnoreCase ) ) )
			.ToList();
		if ( tracks.Any( track => track is null
			|| track.Keys.Count != 1
			|| MathF.Abs( track.Keys[0].Time ) > 0.0001f ) )
			return false;

		var legacyMatches = 0;
		var localMatches = 0;
		var comparableChildren = 0;
		foreach ( var definition in retained.Where( x =>
			!x.Id.Equals( document.Rig.SourceSkeletonRootId, StringComparison.OrdinalIgnoreCase ) ) )
		{
			var track = idle.Tracks.FirstOrDefault( x =>
				x.Target.Equals( definition.Name, StringComparison.OrdinalIgnoreCase ) );
			if ( track is null )
				continue;

			comparableChildren++;
			var keyed = new Transform(
				track.Keys[0].Position,
				track.Keys[0].Rotation,
				track.Keys[0].Scale );
			if ( TransformNear( keyed, definition.BindModelTransform ) )
				legacyMatches++;
			if ( TransformNear( keyed, definition.BindLocalTransform ) )
				localMatches++;
		}

		var childrenUseLegacyModelSpace = comparableChildren >= 1
			&& legacyMatches * 4 >= comparableChildren * 3
			&& legacyMatches > localMatches;
		var sourceRoot = retained.FirstOrDefault( x => x.Id.Equals(
			document.Rig.SourceSkeletonRootId,
			StringComparison.OrdinalIgnoreCase ) );
		var hostRoot = weaponBones.FirstOrDefault( x => x.SourceBoneId.Equals(
			document.Rig.SourceSkeletonRootId,
			StringComparison.OrdinalIgnoreCase ) );
		var rootTrack = hostRoot is null
			? null
			: idle.Tracks.FirstOrDefault( x =>
				x.Target.Equals( hostRoot.Name, StringComparison.OrdinalIgnoreCase ) );
		var rootKey = rootTrack?.Keys.FirstOrDefault();
		var rootUsesLegacyNormalization = false;
		Transform correctedRoot = default;
		if ( sourceRoot is not null && hostRoot is not null && rootKey is not null )
		{
			var currentRoot = new Transform(
				rootKey.Position,
				rootKey.Rotation,
				rootKey.Scale );
			correctedRoot = WeaponAnimationMath.Compose(
				currentRoot,
				sourceRoot.BindModelTransform );
			rootUsesLegacyNormalization = childrenUseLegacyModelSpace;

			if ( authoritativeSkeleton is not null )
			{
				var expectedRoot = authoritativeSkeleton.GetBindLocal( hostRoot );
				rootUsesLegacyNormalization |= TransformNear( correctedRoot, expectedRoot )
					&& !TransformNear( currentRoot, expectedRoot );
				if ( rootUsesLegacyNormalization )
					correctedRoot = expectedRoot;
			}
		}

		// Affected projects omitted the imported root bind and seeded children in model space.
		if ( !childrenUseLegacyModelSpace && !rootUsesLegacyNormalization )
			return false;

		foreach ( var bone in weaponBones )
		{
			if ( bone.SourceBoneId.Equals(
				document.Rig.SourceSkeletonRootId,
				StringComparison.OrdinalIgnoreCase ) )
				continue;
			if ( !childrenUseLegacyModelSpace )
				continue;

			var track = idle.Tracks.First( x =>
				x.Target.Equals( bone.Name, StringComparison.OrdinalIgnoreCase ) );
			var local = skeleton.GetBindLocal( bone );
			var key = track.Keys[0];
			key.Position = local.Position;
			key.Rotation = local.Rotation.Normal;
			key.Scale = local.Scale;
		}

		if ( rootUsesLegacyNormalization && rootKey is not null )
		{
			rootKey.Position = correctedRoot.Position;
			rootKey.Rotation = correctedRoot.Rotation.Normal;
			rootKey.Scale = correctedRoot.Scale;
		}

		idle.IsBindPoseSeed = true;
		return true;
	}

	private static bool TransformNear( Transform left, Transform right, float tolerance = 0.001f )
	{
		return left.Position.Distance( right.Position ) <= tolerance
			&& (left.Scale - right.Scale).Length <= tolerance
			&& (left.Rotation.Forward - right.Rotation.Forward).Length <= tolerance
			&& (left.Rotation.Up - right.Rotation.Up).Length <= tolerance;
	}

	public static string CreateBackup( string absoluteAssetPath, int sourceSchemaVersion )
	{
		if ( string.IsNullOrWhiteSpace( absoluteAssetPath ) || !File.Exists( absoluteAssetPath ) )
			throw new FileNotFoundException( "The original .wepanim asset is unavailable for migration backup.", absoluteAssetPath );

		var backup = $"{absoluteAssetPath}.v{sourceSchemaVersion}.bak";
		if ( !File.Exists( backup ) )
			File.Copy( absoluteAssetPath, backup, false );
		return backup;
	}
}
