#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace SboxWeaponAnimator;

public static class WeaponRigHierarchy
{
	public static void RepairMetadata( WeaponRigDefinition rig, bool legacyBindTransforms )
	{
		var byName = rig.Bones
			.GroupBy( x => x.Name, StringComparer.OrdinalIgnoreCase )
			.ToDictionary( x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase );
		var paths = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

		string ResolvePath( WeaponBoneDefinition bone, HashSet<string> visiting )
		{
			if ( paths.TryGetValue( bone.Name, out var existing ) )
				return existing;
			if ( !visiting.Add( bone.Name ) )
				return EscapePathPart( bone.Name );

			var own = EscapePathPart( bone.Name );
			if ( !string.IsNullOrWhiteSpace( bone.ParentName )
				&& byName.TryGetValue( bone.ParentName, out var parent ) )
			{
				own = $"{ResolvePath( parent, visiting )}/{own}";
			}

			visiting.Remove( bone.Name );
			paths[bone.Name] = own;
			return own;
		}

		foreach ( var bone in rig.Bones )
		{
			bone.HierarchyPath = ResolvePath( bone, [] );
			bone.Id = bone.HierarchyPath;
			bone.OriginalName = string.IsNullOrWhiteSpace( bone.OriginalName )
				? bone.Name
				: bone.OriginalName;
			bone.OriginalParentName = string.IsNullOrWhiteSpace( bone.OriginalParentName )
				? bone.ParentName
				: bone.OriginalParentName;

			if ( legacyBindTransforms )
				bone.BindModelTransform = bone.BindTransform;
			else
				bone.BindTransform = bone.BindModelTransform;
		}

		foreach ( var bone in rig.Bones )
		{
			var parent = string.IsNullOrWhiteSpace( bone.ParentName )
				? null
				: rig.FindBone( bone.ParentName );
			bone.ParentId = parent?.Id ?? "";
			bone.BindLocalTransform = parent is null
				? bone.BindModelTransform
				: parent.BindModelTransform.ToLocal( bone.BindModelTransform );
		}

		var sourceRoot = rig.Bones.FirstOrDefault( x => string.IsNullOrWhiteSpace( x.ParentId ) );
		rig.SourceSkeletonRootId = sourceRoot?.Id ?? "";
		var weaponRoot = rig.Bones.FirstOrDefault( x =>
			x.Classification == WeaponBoneClassification.WeaponRoot );
		if ( weaponRoot is not null )
		{
			rig.RootBone = weaponRoot.Name;
			rig.WeaponSubtreeRootId = weaponRoot.Id;
		}
	}

	public static bool SelectWeaponSubtree( WeaponRigDefinition rig, string idOrName )
	{
		var selected = rig.FindBone( idOrName );
		if ( selected is null )
			return false;

		var descendants = DescendantIds( rig, selected.Id );
		var ancestors = AncestorIds( rig, selected );
		foreach ( var bone in rig.Bones )
		{
			if ( bone.Id.Equals( selected.Id, StringComparison.OrdinalIgnoreCase ) )
			{
				bone.Inclusion = WeaponBoneInclusion.Included;
				bone.Classification = WeaponBoneClassification.WeaponRoot;
			}
			else if ( descendants.Contains( bone.Id ) )
			{
				bone.Inclusion = WeaponBoneInclusion.Included;
				if ( bone.Classification is WeaponBoneClassification.Ignored
					or WeaponBoneClassification.WeaponRoot )
					bone.Classification = WeaponBoneClassification.Animatable;
			}
			else if ( ancestors.Contains( bone.Id ) )
			{
				bone.Inclusion = WeaponBoneInclusion.StructuralBridge;
				bone.Classification = WeaponBoneClassification.Structural;
			}
			else
			{
				bone.Inclusion = WeaponBoneInclusion.Excluded;
				bone.Classification = WeaponBoneClassification.Ignored;
			}
		}

		rig.RootBone = selected.Name;
		rig.WeaponSubtreeRootId = selected.Id;
		RequireReview( rig );
		return true;
	}

	public static bool ExcludeBranch( WeaponRigDefinition rig, string idOrName )
	{
		var selected = rig.FindBone( idOrName );
		if ( selected is null || string.IsNullOrWhiteSpace( rig.WeaponSubtreeRootId ) )
			return false;

		var protectedIds = AncestorIds(
			rig,
			rig.FindBone( rig.WeaponSubtreeRootId ) ?? selected );
		protectedIds.Add( rig.WeaponSubtreeRootId );
		if ( protectedIds.Contains( selected.Id ) )
			return false;

		var branch = DescendantIds( rig, selected.Id );
		branch.Add( selected.Id );
		foreach ( var bone in rig.Bones.Where( x => branch.Contains( x.Id ) ) )
		{
			bone.Inclusion = WeaponBoneInclusion.Excluded;
			bone.Classification = WeaponBoneClassification.Ignored;
		}

		RequireReview( rig );
		return true;
	}

	public static bool IncludeBranch( WeaponRigDefinition rig, string idOrName )
	{
		var selected = rig.FindBone( idOrName );
		var root = rig.FindBone( rig.WeaponSubtreeRootId );
		if ( selected is null || root is null )
			return false;

		var rootBranch = DescendantIds( rig, root.Id );
		rootBranch.Add( root.Id );
		if ( !rootBranch.Contains( selected.Id ) )
			return false;

		var branch = DescendantIds( rig, selected.Id );
		branch.Add( selected.Id );
		foreach ( var bone in rig.Bones.Where( x => branch.Contains( x.Id ) ) )
		{
			bone.Inclusion = WeaponBoneInclusion.Included;
			bone.Classification = bone.Id.Equals( root.Id, StringComparison.OrdinalIgnoreCase )
				? WeaponBoneClassification.WeaponRoot
				: WeaponBoneClassification.Animatable;
		}

		RequireReview( rig );
		return true;
	}

	public static void ConfirmFilteredPreview( WeaponRigDefinition rig )
	{
		rig.ReviewRequired = false;
		rig.FilteredPreviewConfirmed = true;
	}

	public static bool IsRetained( WeaponBoneDefinition bone ) =>
		bone.Inclusion != WeaponBoneInclusion.Excluded
		&& bone.Classification != WeaponBoneClassification.Ignored;

	public static string ProfileText( WeaponRigDefinition rig ) => string.Join(
		"\n",
		rig.Bones
			.OrderBy( x => x.Id, StringComparer.OrdinalIgnoreCase )
			.Select( x =>
				$"{x.Id}|{x.ParentId}|{x.Name}|{x.Classification}|{x.Inclusion}|"
				+ $"{x.BindModelTransform}|{x.BindLocalTransform}" ) );

	private static HashSet<string> DescendantIds( WeaponRigDefinition rig, string rootId )
	{
		var result = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var pending = new Queue<string>();
		pending.Enqueue( rootId );
		while ( pending.Count > 0 )
		{
			var parentId = pending.Dequeue();
			foreach ( var child in rig.Bones.Where( x =>
				x.ParentId.Equals( parentId, StringComparison.OrdinalIgnoreCase ) ) )
			{
				if ( result.Add( child.Id ) )
					pending.Enqueue( child.Id );
			}
		}
		return result;
	}

	private static HashSet<string> AncestorIds(
		WeaponRigDefinition rig,
		WeaponBoneDefinition bone )
	{
		var result = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var parentId = bone.ParentId;
		while ( !string.IsNullOrWhiteSpace( parentId ) && result.Add( parentId ) )
			parentId = rig.FindBone( parentId )?.ParentId ?? "";
		return result;
	}

	private static void RequireReview( WeaponRigDefinition rig )
	{
		rig.ReviewRequired = true;
		rig.FilteredPreviewConfirmed = false;
	}

	private static string EscapePathPart( string value ) =>
		value.Replace( "%", "%25", StringComparison.Ordinal )
			.Replace( "/", "%2F", StringComparison.Ordinal );
}
