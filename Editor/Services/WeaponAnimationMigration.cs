#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SboxWeaponAnimator.Editor;

public sealed class WeaponAnimationMigrationResult
{
	public bool Migrated { get; init; }
	public bool RepairedLegacyIdle { get; init; }
	public int SourceSchemaVersion { get; init; }
	public int PreservedWeaponTracks { get; init; }
	public int RemovedTracks { get; init; }
	public int RemovedConstraints { get; init; }
	public bool Changed => Migrated || RepairedLegacyIdle;

	public string Summary
	{
		get
		{
			if ( Migrated )
			{
				return $"Migrated schema {SourceSchemaVersion} → {WeaponAnimationDocument.CurrentSchemaVersion}. "
					+ $"Preserved {PreservedWeaponTracks} weapon tracks; reset {RemovedTracks} arm or incompatible tracks"
					+ (RemovedConstraints > 0 ? $" and {RemovedConstraints} constraints." : ".");
			}

			return RepairedLegacyIdle
				? "Repaired a legacy Idle bind pose that stored weapon bones in model space. "
					+ "The original version will be backed up when this project is saved."
				: "";
		}
	}
}

public static class WeaponAnimationMigration
{
	public static WeaponAnimationMigrationResult MigrateAndRepair(
		WeaponAnimationDocument document )
	{
		if ( document.SchemaVersion > WeaponAnimationDocument.CurrentSchemaVersion )
		{
			throw new InvalidOperationException(
				$"This .wepanim uses schema {document.SchemaVersion}, newer than supported schema "
				+ $"{WeaponAnimationDocument.CurrentSchemaVersion}." );
		}

		var sourceVersion = document.SchemaVersion;
		var migrated = sourceVersion < 3;
		var preservedTracks = 0;
		var removedTracks = 0;
		var removedConstraints = 0;

		if ( migrated )
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

		var repairedLegacyIdle = !migrated && RepairLegacyIdleBindPose( document );
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
			RepairedLegacyIdle = repairedLegacyIdle,
			SourceSchemaVersion = sourceVersion,
			PreservedWeaponTracks = preservedTracks,
			RemovedTracks = removedTracks,
			RemovedConstraints = removedConstraints
		};
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
