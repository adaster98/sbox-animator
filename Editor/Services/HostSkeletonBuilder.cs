#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
	public int ArmSide { get; set; }
}

public sealed class HostSkeleton
{
	public List<HostBone> Bones { get; } = [];
	public Dictionary<string, HostBone> ByName { get; } = new( StringComparer.OrdinalIgnoreCase );
	public Dictionary<string, List<HostBone>> ChildrenByParent { get; } =
		new( StringComparer.OrdinalIgnoreCase );

	public void Add( HostBone bone )
	{
		if ( ByName.ContainsKey( bone.Name ) )
			return;

		bone.ArmSide = DirectArmSide( bone.Name );
		if ( bone.ArmSide == 0
			&& ByName.TryGetValue( bone.ParentName, out var parent ) )
			bone.ArmSide = parent.ArmSide;
		bone.Index = Bones.Count;
		Bones.Add( bone );
		ByName.Add( bone.Name, bone );
		if ( !ChildrenByParent.TryGetValue( bone.ParentName, out var children ) )
		{
			children = [];
			ChildrenByParent[bone.ParentName] = children;
		}
		children.Add( bone );
	}

	public IReadOnlyList<HostBone> ChildrenOf( string parentName ) =>
		ChildrenByParent.GetValueOrDefault( parentName ) ?? [];

	public Transform GetBindLocal( HostBone bone )
	{
		if ( bone.HasExplicitBindLocal )
			return bone.BindLocalTransform;

		if ( string.IsNullOrWhiteSpace( bone.ParentName )
			|| !ByName.TryGetValue( bone.ParentName, out var parent ) )
			return bone.BindModelTransform;

		return parent.BindModelTransform.ToLocal( bone.BindModelTransform );
	}

	public void RebuildModelTransformsFromLocals()
	{
		var pending = Bones.ToList();
		var rebuilt = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		while ( pending.Count > 0 )
		{
			var progressed = false;
			for ( var i = pending.Count - 1; i >= 0; i-- )
			{
				var bone = pending[i];
				if ( !string.IsNullOrWhiteSpace( bone.ParentName )
					&& ByName.ContainsKey( bone.ParentName )
					&& !rebuilt.Contains( bone.ParentName ) )
				{
					continue;
				}

				var local = GetBindLocal( bone );
				bone.BindModelTransform = string.IsNullOrWhiteSpace( bone.ParentName )
					|| !ByName.TryGetValue( bone.ParentName, out var parent )
						? local
						: ComposeLocal( parent.BindModelTransform, local );
				rebuilt.Add( bone.Name );
				pending.RemoveAt( i );
				progressed = true;
			}

			if ( progressed )
				continue;

			throw new InvalidOperationException(
				$"The animation host contains a cyclic bone hierarchy near '{pending[0].Name}'." );
		}

		ChildrenByParent.Clear();
		foreach ( var bone in Bones )
		{
			if ( !ChildrenByParent.TryGetValue( bone.ParentName, out var children ) )
			{
				children = [];
				ChildrenByParent[bone.ParentName] = children;
			}
			children.Add( bone );
		}

		foreach ( var bone in Bones )
			bone.ArmSide = ResolveArmSide( bone );
	}

	public IReadOnlyDictionary<string, Transform> BuildCompilerBindModelTransforms()
	{
		// Render-mesh import scale is already baked into physical bone positions. Preserve those
		// pivots while exposing the scale-one skeleton expected by ModelDoc and runtime skinning.
		return Bones.ToDictionary(
			bone => bone.Name,
			bone => bone.BindModelTransform.WithScale( Vector3.One ),
			StringComparer.OrdinalIgnoreCase );
	}

	public IReadOnlyDictionary<string, Transform> BuildCompilerBindLocalTransforms()
	{
		var model = BuildCompilerBindModelTransforms();
		return Bones.ToDictionary(
			bone => bone.Name,
			bone => string.IsNullOrWhiteSpace( bone.ParentName )
				|| !model.TryGetValue( bone.ParentName, out var parent )
					? model[bone.Name]
					: parent.ToLocal( model[bone.Name] ),
			StringComparer.OrdinalIgnoreCase );
	}

	private static Transform ComposeLocal( Transform parent, Transform local ) => new(
		parent.PointToWorld( local.Position ),
		parent.Rotation * local.Rotation,
		parent.Scale * local.Scale );

	private int ResolveArmSide( HostBone bone )
	{
		var current = bone;
		for ( var depth = 0; depth <= Bones.Count; depth++ )
		{
			var direct = DirectArmSide( current.Name );
			if ( direct != 0 )
				return direct;
			if ( string.IsNullOrWhiteSpace( current.ParentName )
				|| !ByName.TryGetValue( current.ParentName, out current ) )
				return 0;
			if ( current.ArmSide != 0 )
				return current.ArmSide;
		}
		return 0;
	}

	private static int DirectArmSide( string name ) =>
		name.EndsWith( "_R", StringComparison.OrdinalIgnoreCase )
			? 1
			: name.EndsWith( "_L", StringComparison.OrdinalIgnoreCase )
				? -1
				: 0;
}

public static class HostSkeletonBuilder
{
	public const string ProductionArmsModel = "models/first_person/v_first_person_arms_human.vmdl";
	public const string PreviewArmsModel = "models/first_person/first_person_arms_preview.vmdl";
	private static readonly Dictionary<(Guid DocumentId, bool Arms), CachedHostSkeleton>
		SkeletonCache = [];

	/// <summary>
	/// Shared, cached host skeleton. The returned instance is handed to every caller, so treat it
	/// as read-only — mutating it corrupts the viewport's live pose evaluation and every other
	/// holder. Call <see cref="Build"/> when you need an instance you own.
	/// </summary>
	public static HostSkeleton BuildCached(
		WeaponAnimationDocument document,
		bool includeArmProfile = true )
	{
		var key = (document.DocumentId, includeArmProfile);
		var signature = CacheSignature( document, includeArmProfile );
		if ( SkeletonCache.TryGetValue( key, out var cached )
			&& cached.Signature.Equals( signature, StringComparison.Ordinal ) )
			return cached.Skeleton;

		var skeleton = Build( document, includeArmProfile );
		if ( SkeletonCache.Count >= 8 && !SkeletonCache.ContainsKey( key ) )
			SkeletonCache.Clear();
		SkeletonCache[key] = new CachedHostSkeleton( signature, skeleton );
		return skeleton;
	}

	public static void ClearCache() => SkeletonCache.Clear();

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

		// Replacing Facepunch's weapon_root changes every canonical helper below it. Their local
		// bind transforms remain authoritative, so rebuild model-space values before validation,
		// pose evaluation, and export inspect the combined hierarchy.
		skeleton.RebuildModelTransformsFromLocals();
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

	/// <summary>
	/// Which arm profile <see cref="LoadArmProfile"/> would resolve to right now, or "" when none
	/// is loadable. Used to keep the skeleton cache honest across load-order changes.
	/// </summary>
	private static string ResolvedArmProfileName()
	{
		try
		{
			var profile = LoadArmProfile();
			return profile is null ? "" : profile.Name ?? ProductionArmsModel;
		}
		catch
		{
			return "";
		}
	}

	public static Model? LoadArmProfile()
	{
		var production = Model.Load( ProductionArmsModel );
		if ( production is not null && !production.IsError )
			return production;

		var preview = Model.Load( PreviewArmsModel );
		return preview is not null && !preview.IsError ? preview : null;
	}

	private static string CacheSignature(
		WeaponAnimationDocument document,
		bool includeArmProfile )
	{
		var builder = new StringBuilder( 256 + document.Rig.Bones.Count * 128 );
		builder.Append( includeArmProfile ).Append( '|' )
			// The arm profile degrades production -> preview -> no arms at all. Without it in the
			// signature, a skeleton built before the arms model was loadable stays cached, and the
			// viewport silently keeps an armless rig until some document field happens to change.
			.Append( includeArmProfile ? ResolvedArmProfileName() : "" ).Append( '|' )
			.Append( document.Rig.SourceSkeletonRootId ).Append( '|' );
		AppendTransform( builder, document.Calibration.PhysicalTransform );
		AppendTransform( builder, document.Calibration.FramingTransform );
		AppendTransform( builder, document.Binding.PrimaryHand.Transform );
		AppendTransform( builder, document.Binding.SupportHand.Transform );
		foreach ( var bone in document.Rig.Bones )
		{
			builder.Append( '\n' )
				.Append( bone.Id ).Append( '|' )
				.Append( bone.Name ).Append( '|' )
				.Append( bone.ParentId ).Append( '|' )
				.Append( bone.ParentName ).Append( '|' )
				.Append( bone.Classification ).Append( '|' )
				.Append( bone.Inclusion ).Append( '|' );
			AppendTransform( builder, bone.BindModelTransform );
			AppendTransform( builder, bone.BindLocalTransform );
		}
		return builder.ToString();
	}

	private static void AppendTransform( StringBuilder builder, Transform transform )
	{
		AppendFloat( builder, transform.Position.x );
		AppendFloat( builder, transform.Position.y );
		AppendFloat( builder, transform.Position.z );
		AppendFloat( builder, transform.Rotation.x );
		AppendFloat( builder, transform.Rotation.y );
		AppendFloat( builder, transform.Rotation.z );
		AppendFloat( builder, transform.Rotation.w );
		AppendFloat( builder, transform.Scale.x );
		AppendFloat( builder, transform.Scale.y );
		AppendFloat( builder, transform.Scale.z );
	}

	private static void AppendFloat( StringBuilder builder, float value ) =>
		builder.Append( BitConverter.SingleToInt32Bits( value ) ).Append( ',' );

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

	private sealed record CachedHostSkeleton( string Signature, HostSkeleton Skeleton );
}

public sealed record BindParityIssue(
	string BoneName,
	float PositionDelta,
	float RotationDelta,
	float ScaleDelta );
