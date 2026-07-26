#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public sealed class HostBone
{
	public int Index { get; set; }
	public string Name { get; set; } = "";
	public string ParentName { get; set; } = "";
	public Transform BindModelTransform { get; set; } = Transform.Zero;
	public Transform BindLocalTransform { get; set; } = Transform.Zero;
	public bool HasExplicitBindLocal { get; set; }
	public bool IsWeaponBone { get; set; }
	public string SourceBoneId { get; set; } = "";
}

public sealed class HostSkeleton
{
	public List<HostBone> Bones { get; } = [];
	public Dictionary<string, HostBone> ByName { get; } = new( StringComparer.OrdinalIgnoreCase );

	public void Add( HostBone bone )
	{
		if ( ByName.ContainsKey( bone.Name ) )
			return;

		bone.Index = Bones.Count;
		Bones.Add( bone );
		ByName.Add( bone.Name, bone );
	}

	public Transform GetBindLocal( HostBone bone )
	{
		if ( bone.HasExplicitBindLocal )
			return bone.BindLocalTransform;

		if ( string.IsNullOrWhiteSpace( bone.ParentName )
			|| !ByName.TryGetValue( bone.ParentName, out var parent ) )
			return bone.BindModelTransform;

		return parent.BindModelTransform.ToLocal( bone.BindModelTransform );
	}
}

public static class HostSkeletonBuilder
{
	public const string ProductionArmsModel = "models/first_person/v_first_person_arms_human.vmdl";
	public const string PreviewArmsModel = "models/first_person/first_person_arms_preview.vmdl";

	public static HostSkeleton Build(
		WeaponAnimationDocument document,
		bool includeArmProfile = true )
	{
		var skeleton = new HostSkeleton();
		var arms = includeArmProfile ? LoadArmProfile() : null;

		if ( arms is not null && !arms.IsError )
		{
			foreach ( var bone in arms.Bones.AllBones )
			{
				var local = bone.Parent is null
					? bone.LocalTransform
					: bone.Parent.LocalTransform.ToLocal( bone.LocalTransform );
				skeleton.Add( new HostBone
				{
					Name = bone.Name,
					ParentName = bone.Parent?.Name ?? "",
					BindModelTransform = bone.LocalTransform,
					BindLocalTransform = local,
					HasExplicitBindLocal = true
				} );
			}
		}

		EnsureCoreBone( skeleton, "root", "", Transform.Zero );
		var weaponPlacement = WeaponAnimationMath.Compose(
			document.Calibration.PhysicalTransform,
			document.Calibration.FramingTransform );
		EnsureCoreBone( skeleton, "camera", "root", new Transform( Vector3.Zero ) );
		var sourceRoot = document.Rig.FindBone( document.Rig.SourceSkeletonRootId )
			?? document.Rig.Bones.FirstOrDefault( bone =>
				string.IsNullOrWhiteSpace( bone.ParentId ) );
		var rootModel = sourceRoot is null
			? weaponPlacement
			: ApplyPlacement( weaponPlacement, sourceRoot.BindModelTransform );
		SetCoreBone(
			skeleton,
			"weapon_root",
			"root",
			rootModel,
			true,
			sourceRoot?.Id ?? "" );
		EnsureCoreBone(
			skeleton,
			"ik_hand_R",
			"weapon_root",
			document.Binding.PrimaryHand.Transform );
		EnsureCoreBone(
			skeleton,
			"ik_hand_L",
			"weapon_root",
			document.Binding.SupportHand.Transform );

		foreach ( var definition in TopologicalWeaponBones( document.Rig.Bones ) )
		{
			if ( !WeaponRigHierarchy.IsRetained( definition ) )
				continue;

			// The source root is normalized to the host's canonical weapon root.
			if ( sourceRoot is not null
				&& definition.Id.Equals( sourceRoot.Id, StringComparison.OrdinalIgnoreCase ) )
				continue;

			if ( skeleton.ByName.ContainsKey( definition.Name ) )
				continue;

			var parentName = definition.ParentName;
			if ( sourceRoot is not null
				&& definition.ParentId.Equals( sourceRoot.Id, StringComparison.OrdinalIgnoreCase ) )
				parentName = "weapon_root";
			if ( string.IsNullOrWhiteSpace( parentName )
				|| !skeleton.ByName.ContainsKey( parentName ) )
				parentName = "weapon_root";

			var parentModel = skeleton.ByName[parentName].BindModelTransform;
			var localTransform = definition.BindLocalTransform;
			if ( parentName == "weapon_root"
				&& sourceRoot is not null
				&& !definition.ParentId.Equals( sourceRoot.Id, StringComparison.OrdinalIgnoreCase ) )
			{
				localTransform = sourceRoot.BindModelTransform.ToLocal(
					definition.BindModelTransform );
			}
			var modelTransform = ComposeLocal( parentModel, localTransform );
			skeleton.Add( new HostBone
			{
				Name = definition.Name,
				ParentName = parentName,
				BindModelTransform = modelTransform,
				BindLocalTransform = localTransform,
				HasExplicitBindLocal = true,
				IsWeaponBone = true,
				SourceBoneId = definition.Id
			} );
		}

		return skeleton;
	}

	public static IReadOnlyList<BindParityIssue> ValidateBindParity(
		WeaponAnimationDocument document,
		float tolerance = 0.001f,
		bool includeArmProfile = true )
	{
		var issues = new List<BindParityIssue>();
		var skeleton = Build( document, includeArmProfile );
		var placement = WeaponAnimationMath.Compose(
			document.Calibration.PhysicalTransform,
			document.Calibration.FramingTransform );

		foreach ( var definition in document.Rig.RetainedBones() )
		{
			var hostName = definition.Id.Equals(
				document.Rig.SourceSkeletonRootId,
				StringComparison.OrdinalIgnoreCase )
				? "weapon_root"
				: definition.Name;
			if ( !skeleton.ByName.TryGetValue( hostName, out var host ) )
			{
				issues.Add( new BindParityIssue(
					definition.Name,
					float.PositiveInfinity,
					float.PositiveInfinity,
					float.PositiveInfinity ) );
				continue;
			}

			var expected = ApplyPlacement( placement, definition.BindModelTransform );
			var positionDelta = expected.Position.Distance( host.BindModelTransform.Position );
			var rotationDelta = MathF.Max(
				(expected.Rotation.Forward - host.BindModelTransform.Rotation.Forward).Length,
				(expected.Rotation.Up - host.BindModelTransform.Rotation.Up).Length );
			var scaleDelta = (expected.Scale - host.BindModelTransform.Scale).Length;
			if ( positionDelta > tolerance || rotationDelta > tolerance || scaleDelta > tolerance )
			{
				issues.Add( new BindParityIssue(
					definition.Name,
					positionDelta,
					rotationDelta,
					scaleDelta ) );
			}
		}

		return issues;
	}

	public static IReadOnlyList<string> FindArmBoneCollisions(
		WeaponAnimationDocument document )
	{
		var arms = LoadArmProfile();
		if ( arms is null || arms.IsError )
			return [];

		var armNames = arms.Bones.AllBones
			.Select( x => x.Name )
			.ToHashSet( StringComparer.OrdinalIgnoreCase );
		return document.Rig.RetainedBones()
			.Where( x => !x.Id.Equals(
					document.Rig.SourceSkeletonRootId,
					StringComparison.OrdinalIgnoreCase )
				&& armNames.Contains( x.Name ) )
			.Select( x => x.Name )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.OrderBy( x => x, StringComparer.OrdinalIgnoreCase )
			.ToList();
	}

	public static Model? LoadArmProfile()
	{
		var production = Model.Load( ProductionArmsModel );
		if ( production is not null && !production.IsError )
			return production;

		var preview = Model.Load( PreviewArmsModel );
		return preview is not null && !preview.IsError ? preview : null;
	}

	private static Transform ApplyPlacement( Transform placement, Transform sourceModel )
	{
		return new Transform(
			placement.PointToWorld( sourceModel.Position ),
			placement.Rotation * sourceModel.Rotation,
			placement.Scale * sourceModel.Scale );
	}

	private static void EnsureCoreBone(
		HostSkeleton skeleton,
		string name,
		string parent,
		Transform transform )
	{
		if ( skeleton.ByName.ContainsKey( name ) )
			return;

		skeleton.Add( new HostBone
		{
			Name = name,
			ParentName = parent,
			BindModelTransform = transform,
			BindLocalTransform = string.IsNullOrWhiteSpace( parent )
				? transform
				: skeleton.ByName.TryGetValue( parent, out var parentBone )
					? parentBone.BindModelTransform.ToLocal( transform )
					: transform,
			HasExplicitBindLocal = true
		} );
	}

	private static void SetCoreBone(
		HostSkeleton skeleton,
		string name,
		string parent,
		Transform transform,
		bool weaponBone = false,
		string sourceBoneId = "" )
	{
		var local = string.IsNullOrWhiteSpace( parent )
			? transform
			: skeleton.ByName.TryGetValue( parent, out var parentBone )
				? parentBone.BindModelTransform.ToLocal( transform )
				: transform;
		if ( skeleton.ByName.TryGetValue( name, out var existing ) )
		{
			existing.ParentName = parent;
			existing.BindModelTransform = transform;
			existing.BindLocalTransform = local;
			existing.HasExplicitBindLocal = true;
			existing.IsWeaponBone = weaponBone;
			existing.SourceBoneId = sourceBoneId;
			return;
		}

		skeleton.Add( new HostBone
		{
			Name = name,
			ParentName = parent,
			BindModelTransform = transform,
			BindLocalTransform = local,
			HasExplicitBindLocal = true,
			IsWeaponBone = weaponBone,
			SourceBoneId = sourceBoneId
		} );
	}

	private static Transform ComposeLocal( Transform parent, Transform local ) => new(
		parent.PointToWorld( local.Position ),
		parent.Rotation * local.Rotation,
		parent.Scale * local.Scale );

	private static IEnumerable<WeaponBoneDefinition> TopologicalWeaponBones(
		IEnumerable<WeaponBoneDefinition> definitions )
	{
		var pending = definitions.ToList();
		var emitted = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		while ( pending.Count > 0 )
		{
			var progressed = false;
			for ( var i = pending.Count - 1; i >= 0; i-- )
			{
				var bone = pending[i];
				if ( !string.IsNullOrWhiteSpace( bone.ParentName )
					&& pending.Any( x => x.Name.Equals( bone.ParentName, StringComparison.OrdinalIgnoreCase ) )
					&& !emitted.Contains( bone.ParentName ) )
					continue;

				yield return bone;
				emitted.Add( bone.Name );
				pending.RemoveAt( i );
				progressed = true;
			}

			if ( progressed )
				continue;

			foreach ( var bone in pending )
				yield return bone;
			yield break;
		}
	}
}

public sealed record BindParityIssue(
	string BoneName,
	float PositionDelta,
	float RotationDelta,
	float ScaleDelta );
