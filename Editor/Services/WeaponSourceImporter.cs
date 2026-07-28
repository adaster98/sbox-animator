#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Editor;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public sealed class SourceImportResult
{
	public bool Success { get; init; }
	public string Message { get; init; } = "";
	public string ModelPath { get; init; } = "";
	public Model? Model { get; init; }
	public List<RigAuditIssue> Issues { get; init; } = [];
}

public sealed class WeaponSourceImporter
{
	private static readonly HashSet<string> SupportedExtensions =
		new( StringComparer.OrdinalIgnoreCase ) { ".fbx", ".smd", ".dmx", ".vmdl" };

	public SourceImportResult Import( WeaponAnimationDocument document, string selectedPath )
	{
		if ( string.IsNullOrWhiteSpace( selectedPath ) )
			return Fail( "No source file was selected." );

		var extension = Path.GetExtension( selectedPath );
		if ( !SupportedExtensions.Contains( extension ) )
			return Fail( "Choose a rigged FBX, SMD, DMX, or VMDL asset." );

		var absoluteSource = ResolveAbsolutePath( selectedPath );
		if ( string.IsNullOrWhiteSpace( absoluteSource ) || !File.Exists( absoluteSource ) )
			return Fail( $"Source file does not exist: {selectedPath}" );

		var importStage = "preparing the source";
		var cacheRoot = "";
		try
		{
			importStage = "hashing the source file";
			var sourceHash = HashFile( absoluteSource );
			importStage = "creating the preview cache";
			cacheRoot = GetPreviewCacheRoot( document.DocumentId );
			Directory.CreateDirectory( cacheRoot );

			importStage = "registering the source asset";
			var cachedSource = EnsureSourceInsideAssets( absoluteSource, cacheRoot, sourceHash );
			var sourceAsset = AssetSystem.RegisterFile( cachedSource );
			if ( sourceAsset is null )
				return Fail( "The Asset System could not register the source file." );

			Asset modelAsset;
			var needsWrapper = !extension.Equals( ".vmdl", StringComparison.OrdinalIgnoreCase );
			if ( needsWrapper )
			{
				importStage = "creating the ModelDoc wrapper";
				var wrapperPath = Path.Combine( cacheRoot, $"source_{sourceHash[..12]}.vmdl" );
				modelAsset = AssetSystem.FindByPath( RelativeAssetPath( wrapperPath ) );
				if ( modelAsset is null )
				{
					modelAsset = EditorUtility.CreateModelFromMeshFile( sourceAsset, wrapperPath );
					if ( modelAsset is null )
						return Fail( "ModelDoc could not create the source VMDL wrapper." );
				}
			}
			else
			{
				importStage = "compiling the VMDL";
				modelAsset = sourceAsset;
				modelAsset.Compile( true );
			}

			importStage = "compiling the model";
			if ( !modelAsset.IsCompiled && !modelAsset.Compile( true ) )
				return Fail( "The source model failed to compile." );

			importStage = "loading the compiled model";
			var model = Model.Load( modelAsset.Path );
			if ( model is null || model.IsError )
				return Fail( $"The compiled model could not be loaded: {modelAsset.Path}" );

			importStage = "auditing the skeleton";
			var bones = AuditBones( model, out var rootName, out var issues );
			var sourceRootBoneName = rootName;
			if ( needsWrapper
				&& !string.IsNullOrWhiteSpace( rootName )
				&& !rootName.Equals( "weapon_root", StringComparison.OrdinalIgnoreCase )
				&& bones.All( bone => !bone.Name.Equals( "weapon_root", StringComparison.OrdinalIgnoreCase ) ) )
			{
				importStage = "normalizing the weapon root";
				var wrapperPath = Path.Combine( cacheRoot, $"source_{sourceHash[..12]}.vmdl" );
				AtomicFile.WriteAllText(
					wrapperPath,
					ModelDocWriter.WriteSourceWrapper( RelativeAssetPath( cachedSource ), rootName ) );
				modelAsset = AssetSystem.RegisterFile( wrapperPath ) ?? modelAsset;
				if ( !modelAsset.Compile( true ) )
					return Fail( $"Could not rename source root '{rootName}' to 'weapon_root'." );

				model = Model.Load( modelAsset.Path );
				if ( model is null || model.IsError )
					return Fail( "The root-normalized source wrapper did not reload." );
				bones = AuditBones( model, out rootName, out issues );
			}

			if ( bones.FirstOrDefault( x =>
				x.Classification == WeaponBoneClassification.WeaponRoot ) is { } normalizedRoot )
			{
				normalizedRoot.OriginalName = sourceRootBoneName;
			}

			document.Source.OriginalSourcePath = selectedPath;
			document.Source.SourcePath = RelativeAssetPath( cachedSource );
			document.Source.CompiledModelPath = modelAsset.Path;
			document.Source.SourceHash = sourceHash;
			document.Source.SourceRootBoneName = sourceRootBoneName;
			document.Source.NeedsModelDocWrapper = needsWrapper;
			document.Source.Compiled = model.BoneCount > 0 && !model.IsError;
			document.Source.PreviewHostCompiled = false;
			document.Source.LastImportedUtc = DateTime.UtcNow;
			document.Rig.RootBone = rootName;
			document.Rig.Bones = bones;
			document.Rig.AuditIssues = issues;
			WeaponRigHierarchy.RepairMetadata( document.Rig, false );
			WeaponRigHierarchy.SelectWeaponSubtree( document.Rig, rootName );
			document.Rig.ProfileHash = HashText( WeaponRigHierarchy.ProfileText( document.Rig ) );
			document.Calibration.Confirmed = false;
			document.Calibration.Snapshot = null;

			return new SourceImportResult
			{
				Success = document.Source.Compiled,
				Message = document.Source.Compiled
					? $"Imported {model.BoneCount} bones from {Path.GetFileName( selectedPath )}."
					: "The model has no usable skeleton.",
				ModelPath = modelAsset.Path,
				Model = model,
				Issues = issues
			};
		}
		catch ( Exception ex )
		{
			Log.Error(
				$"[Weapon Animator] source import failed while {importStage}. "
				+ $"Selected='{selectedPath}', resolved='{absoluteSource}', cache='{cacheRoot}'. {ex}" );
			return Fail( $"Import failed while {importStage}: {ex.Message}" );
		}
	}

	private static List<WeaponBoneDefinition> AuditBones(
		Model model,
		out string rootName,
		out List<RigAuditIssue> issues )
	{
		issues = [];
		var allBones = model.Bones.AllBones;
		rootName = allBones.FirstOrDefault( x =>
			x.Name.Equals( "weapon_root", StringComparison.OrdinalIgnoreCase ) )?.Name
			?? allBones.FirstOrDefault( x => x.Parent is null )?.Name
			?? "";

		if ( allBones.Count == 0 )
		{
			issues.Add( Issue(
				"missing_skinning",
				"The model exposes no bones or skinning data.",
				ValidationSeverity.Error ) );
		}

		var rootCount = allBones.Count( x => x.Parent is null );
		if ( rootCount != 1 )
		{
			issues.Add( Issue(
				"invalid_roots",
				$"Expected one skeleton root but found {rootCount}.",
				ValidationSeverity.Error ) );
		}

		foreach ( var duplicate in allBones
			.GroupBy( x => x.Name, StringComparer.OrdinalIgnoreCase )
			.Where( x => x.Count() > 1 ) )
		{
			issues.Add( Issue(
				"duplicate_bone",
				$"Duplicate bone name '{duplicate.Key}'.",
				ValidationSeverity.Error,
				duplicate.Key ) );
		}

		var selectedRoot = rootName;
		var result = allBones.Select( bone => new WeaponBoneDefinition
		{
			Name = bone.Name,
			ParentName = bone.Parent?.Name ?? "",
			OriginalName = bone.Name,
			OriginalParentName = bone.Parent?.Name ?? "",
			Classification = bone.Name.Equals( selectedRoot, StringComparison.OrdinalIgnoreCase )
				? WeaponBoneClassification.WeaponRoot
				: WeaponBoneClassification.Animatable,
			Inclusion = WeaponBoneInclusion.Included,
			BindTransform = bone.LocalTransform,
			BindModelTransform = bone.LocalTransform,
			BindLocalTransform = bone.Parent is null
				? bone.LocalTransform
				: bone.Parent.LocalTransform.ToLocal( bone.LocalTransform ),
			HasSkinInfluence = true
		} ).ToList();
		var rig = new WeaponRigDefinition
		{
			RootBone = rootName,
			Bones = result
		};
		WeaponRigHierarchy.RepairMetadata( rig, false );
		return result;
	}

	private static string EnsureSourceInsideAssets( string source, string cacheRoot, string hash )
	{
		var assetsRoot = GetContentRoot().NormalizeFilename( false );
		var normalized = source.NormalizeFilename( false );
		var relative = Path.GetRelativePath( assetsRoot, normalized );
		if ( !Path.IsPathRooted( relative )
			&& !relative.Equals( "..", StringComparison.Ordinal )
			&& !relative.StartsWith( $"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal ) )
			return source;

		var filename = $"{Path.GetFileNameWithoutExtension( source )}_{hash[..12]}{Path.GetExtension( source )}";
		var destination = Path.Combine( cacheRoot, filename );
		if ( !File.Exists( destination ) || HashFile( destination ) != hash )
			File.Copy( source, destination, true );
		return destination;
	}

	public static string GetPreviewCacheRoot( Guid documentId )
	{
		return Path.Combine( GetContentRoot(), ".weaponanim-cache", documentId.ToString( "N" ) );
	}

	private static string ResolveAbsolutePath( string path )
	{
		if ( Path.IsPathRooted( path ) )
			return Path.GetFullPath( path );
		return Path.GetFullPath( Path.Combine( GetContentRoot(), path.TrimStart( '/', '\\' ) ) );
	}

	public static string RelativeAssetPath( string absolute )
	{
		return Path.GetRelativePath( GetContentRoot(), absolute ).Replace( '\\', '/' );
	}

	internal static string GetContentRoot()
	{
		var root = global::Editor.FileSystem.Content.GetFullPath( "/" );
		if ( string.IsNullOrWhiteSpace( root ) )
			throw new InvalidOperationException( "The current project's Assets directory is unavailable." );
		return Path.GetFullPath( root );
	}

	private static SourceImportResult Fail( string message ) => new()
	{
		Success = false,
		Message = message
	};

	private static RigAuditIssue Issue(
		string code,
		string message,
		ValidationSeverity severity,
		string bone = "" ) => new()
	{
		Code = code,
		Message = message,
		Severity = severity,
		BoneName = bone
	};

	public static string HashFile( string path )
	{
		using var stream = File.OpenRead( path );
		return Convert.ToHexString( SHA256.HashData( stream ) ).ToLowerInvariant();
	}

	public static string HashText( string value ) =>
		Convert.ToHexString( SHA256.HashData( System.Text.Encoding.UTF8.GetBytes( value ) ) )
			.ToLowerInvariant();
}
