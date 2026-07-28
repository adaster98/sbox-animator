#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
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
	public List<SourceMaterialBinding> Materials { get; init; } = [];
}

public sealed class WeaponSourceImporter
{
	private static readonly HashSet<string> SupportedExtensions =
		new( StringComparer.OrdinalIgnoreCase ) { ".fbx", ".smd", ".dmx", ".vmdl" };

	internal readonly record struct PreviewModelRecoveryCandidate(
		string AbsolutePath,
		DateTime LastWriteUtc,
		bool HasCompiledArtifact,
		bool HasVersionMarker );

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
			var materials = new List<SourceMaterialBinding>();
			if ( needsWrapper )
			{
				importStage = "discovering nearby weapon textures";
				materials = WeaponMaterialPipeline.DiscoverAndPreparePreview(
					absoluteSource,
					cacheRoot,
					modelAsset,
					model,
					issues );

				importStage = "applying source material slots";
				var wrapperPath = Path.Combine( cacheRoot, $"source_{sourceHash[..12]}.vmdl" );
				var canNormalizeRoot =
					!string.IsNullOrWhiteSpace( rootName )
					&& !rootName.Equals( "weapon_root", StringComparison.OrdinalIgnoreCase )
					&& bones.All( bone => !bone.Name.Equals(
						"weapon_root",
						StringComparison.OrdinalIgnoreCase ) );
				AtomicFile.WriteAllText(
					wrapperPath,
					ModelDocWriter.WriteSourceWrapper(
						RelativeAssetPath( cachedSource ),
						canNormalizeRoot ? rootName : "",
						materialRemaps: WeaponMaterialPipeline.PreviewRemaps( materials ) ) );
				modelAsset = AssetSystem.RegisterFile( wrapperPath ) ?? modelAsset;
				if ( !modelAsset.Compile( true ) )
					return Fail( "Could not compile the textured source wrapper." );

				model = Model.Load( modelAsset.Path );
				if ( model is null || model.IsError )
					return Fail( "The textured source wrapper did not reload." );
				var materialIssues = issues
					.Where( issue => issue.Code.StartsWith(
						"material.",
						StringComparison.OrdinalIgnoreCase ) )
					.ToArray();
				bones = AuditBones( model, out rootName, out issues );
				issues.AddRange( materialIssues );
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
			document.Source.Materials = materials;
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
					? ImportMessage( model, selectedPath, materials )
					: "The model has no usable skeleton.",
				ModelPath = modelAsset.Path,
				Model = model,
				Issues = issues,
				Materials = materials
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

	public async Task<SourceImportResult> RefreshMaterialsAsync(
		WeaponAnimationDocument document )
	{
		if ( !document.Source.NeedsModelDocWrapper )
			return Fail( "Automatic texture discovery currently applies to imported FBX, SMD, and DMX sources." );

		var selectedPath = new[]
			{
				document.Source.OriginalSourcePath,
				document.Source.SourcePath
			}
			.Where( path => !string.IsNullOrWhiteSpace( path ) )
			.FirstOrDefault( path =>
			{
				try
				{
					return File.Exists( ResolveAbsolutePath( path ) );
				}
				catch
				{
					return false;
				}
			} )
			?? document.Source.OriginalSourcePath;
		var absoluteSource = ResolveAbsolutePath( selectedPath );
		if ( string.IsNullOrWhiteSpace( absoluteSource ) || !File.Exists( absoluteSource ) )
			return Fail( $"Source file does not exist: {selectedPath}" );

		try
		{
			var cacheRoot = GetPreviewCacheRoot( document.DocumentId );
			Directory.CreateDirectory( cacheRoot );
			var sourceHash = string.IsNullOrWhiteSpace( document.Source.SourceHash )
				? HashFile( absoluteSource )
				: document.Source.SourceHash;
			var cachedSource = EnsureSourceInsideAssets(
				absoluteSource,
				cacheRoot,
				sourceHash );
			var legacyWrapperPath = Path.Combine(
				cacheRoot,
				$"source_{sourceHash[..12]}.vmdl" );
			var modelAsset = AssetSystem.FindByPath( document.Source.CompiledModelPath )
				?? AssetSystem.FindByPath( legacyWrapperPath )
				?? AssetSystem.RegisterFile( legacyWrapperPath );
			if ( modelAsset is null )
				return Fail( "The source wrapper is not registered with the Asset System." );

			Model? sourceModel = null;
			try
			{
				sourceModel = modelAsset.LoadResource<Model>() ?? Model.Load( modelAsset.Path );
			}
			catch ( Exception ex )
			{
				Log.Warning(
					$"[Weapon Animator] current source wrapper could not be inspected; "
					+ $"saved and embedded slot names will be used instead: {ex.Message}" );
			}

			var issues = new List<RigAuditIssue>();
			var materials = WeaponMaterialPipeline.DiscoverAndPreparePreview(
				absoluteSource,
				cacheRoot,
				modelAsset,
				sourceModel,
				issues,
				document.Source.Materials.Select( material => material.SourceMaterialPath ) );
			if ( materials.Count == 0 )
				return Fail( "No source material slots or nearby texture sets were found." );

			Log.Info(
				$"[Weapon Animator] material refresh matched "
				+ $"{materials.Count( material => material.HasUsableTextures )} of "
				+ $"{materials.Count} slot(s) in preview revision "
				+ $"'{WeaponMaterialPipeline.PreviewRevision( materials )}'." );
			foreach ( var textureAbsolute in
				WeaponMaterialPipeline.PreviewTextureAbsolutePaths( materials ) )
			{
				var textureAsset = AssetSystem.RegisterFile( textureAbsolute );
				if ( textureAsset is null
					|| textureAsset.IsDeleted
					|| !textureAsset.HasSourceFile )
					return Fail(
						$"The preview texture could not be registered: "
						+ $"{RelativeAssetPath( textureAbsolute )}" );
			}
			// Give the directory watcher one frame to retain the newly registered image
			// sources before asking MaterialCompiler to consume them.
			await Task.Delay( 16 );

			foreach ( var materialAbsolute in
				WeaponMaterialPipeline.PreviewMaterialAbsolutePaths( materials ) )
			{
				var materialAsset = AssetSystem.RegisterFile( materialAbsolute );
				if ( materialAsset is null )
					return Fail(
						$"The preview material could not be registered: "
						+ $"{RelativeAssetPath( materialAbsolute )}" );
				if ( !await AssetGenerationService.WaitForCompileAsync(
					materialAsset,
					materialAbsolute ) )
				{
					Log.Error(
						$"[Weapon Animator] preview material compilation failed: "
						+ $"'{materialAsset.Path}'." );
					return Fail(
						$"Preview material compilation failed: {materialAsset.Path}" );
				}
			}

			var revisionRoot = WeaponMaterialPipeline.PreviewRevisionRoot(
				cacheRoot,
				materials );
			var modelRoot = Path.Combine( revisionRoot, "models" );
			Directory.CreateDirectory( modelRoot );
			var wrapperPath = MaterialCandidateWrapperPath(
				cacheRoot,
				sourceHash,
				materials );
			AtomicFile.WriteAllText(
				wrapperPath,
				ModelDocWriter.WriteSourceWrapper(
					RelativeAssetPath( cachedSource ),
					document.Rig.Bones.All( bone =>
						!bone.Name.Equals(
							"weapon_root",
							StringComparison.OrdinalIgnoreCase )
						|| bone.Name.Equals(
							document.Source.SourceRootBoneName,
							StringComparison.OrdinalIgnoreCase ) )
								? document.Source.SourceRootBoneName
								: "",
					materialRemaps: WeaponMaterialPipeline.PreviewRemaps( materials ) ) );
			var candidateAsset = AssetSystem.RegisterFile( wrapperPath );
			if ( candidateAsset is null )
				return Fail( "The candidate textured source wrapper could not be registered." );
			if ( !await AssetGenerationService.WaitForCompileAsync(
				candidateAsset,
				wrapperPath ) )
				return Fail( "The candidate textured source wrapper could not be compiled." );

			var model = await ReloadMaterialPreviewAsync(
				wrapperPath,
				candidateAsset.Path );
			if ( model is null || model.IsError || model.BoneCount == 0 )
				return Fail(
					"The candidate textured source wrapper compiled but did not reload "
					+ "with a usable skeleton." );

			Log.Info(
				$"[Weapon Animator] accepted material preview '{candidateAsset.Path}' "
				+ $"with {model.BoneCount} bones. The active document can now switch safely." );
			return new SourceImportResult
			{
				Success = true,
				Message = ImportMessage( model, selectedPath, materials ),
				ModelPath = candidateAsset.Path,
				Model = model,
				Issues = issues,
				Materials = materials
			};
		}
		catch ( Exception ex )
		{
			Log.Error( $"[Weapon Animator] material refresh failed for '{selectedPath}': {ex}" );
			return Fail( $"Material refresh failed: {ex.Message}" );
		}
	}

	public static void ApplyMaterialRefresh(
		WeaponAnimationDocument document,
		SourceImportResult result )
	{
		if ( !result.Success )
			return;

		document.Source.Materials = result.Materials;
		document.Source.CompiledModelPath = result.ModelPath;
		document.Source.Compiled = result.Model is { IsError: false } && result.Model.BoneCount > 0;
		document.Source.LastImportedUtc = DateTime.UtcNow;
		document.Rig.AuditIssues.RemoveAll( issue => issue.Code.StartsWith(
			"material.",
			StringComparison.OrdinalIgnoreCase ) );
		document.Rig.AuditIssues.AddRange( result.Issues );
	}

	public static bool TryRecoverMissingPreviewModel(
		WeaponAnimationDocument document,
		out string message )
	{
		message = "";
		if ( document.Source is null
			|| !document.Source.NeedsModelDocWrapper
			|| SourceAssetExists( document.Source.CompiledModelPath ) )
			return false;

		var previewRoot = Path.Combine(
			GetContentRoot(),
			"weaponanim_preview_cache",
			document.DocumentId.ToString( "N" ) );
		if ( !Directory.Exists( previewRoot ) )
			return false;

		var sourceHash = document.Source.SourceHash;
		var expectedPrefix = sourceHash.Length >= 12
			? $"source_{sourceHash[..12]}_textured"
			: "source_";
		var candidates = Directory.EnumerateFiles(
				previewRoot,
				"source_*_textured.vmdl",
				SearchOption.AllDirectories )
			.Where( path =>
			{
				var filename = Path.GetFileNameWithoutExtension( path );
				return sourceHash.Length < 12
					? filename.StartsWith( expectedPrefix, StringComparison.OrdinalIgnoreCase )
					: filename.Equals( expectedPrefix, StringComparison.OrdinalIgnoreCase );
			} )
			.Select( path => new PreviewModelRecoveryCandidate(
				path,
				File.GetLastWriteTimeUtc( path ),
				File.Exists( path + "_c" ),
				File.Exists( Path.Combine(
					Path.GetDirectoryName( Path.GetDirectoryName( path ) ) ?? "",
					".weaponanim-preview-version" ) ) ) )
			.ToArray();
		var selected = SelectRecoveryCandidate( candidates );
		if ( string.IsNullOrWhiteSpace( selected ) )
			return false;

		var asset = AssetSystem.RegisterFile( selected );
		Model? model = null;
		try
		{
			model = asset?.LoadResource<Model>()
				?? Model.Load( RelativeAssetPath( selected ) );
		}
		catch ( Exception ex )
		{
			Log.Warning(
				$"[Weapon Animator] recovered preview candidate could not be inspected: "
				+ $"{ex.Message}" );
		}
		if ( asset is null || model is null || model.IsError || model.BoneCount == 0 )
			return false;

		var previous = document.Source.CompiledModelPath;
		document.Source.CompiledModelPath = asset.Path;
		document.Source.Compiled = true;
		document.Source.PreviewHostCompiled = false;
		message =
			$"Recovered the source weapon preview from '{previous}' to '{asset.Path}' "
			+ $"({model.BoneCount} bones). Save the project to retain the repaired path.";
		Log.Warning( $"[Weapon Animator] {message}" );
		return true;
	}

	internal static string SelectRecoveryCandidateForTests(
		IEnumerable<PreviewModelRecoveryCandidate> candidates ) =>
		SelectRecoveryCandidate( candidates );

	private static string SelectRecoveryCandidate(
		IEnumerable<PreviewModelRecoveryCandidate> candidates ) =>
		candidates
			.Where( candidate => candidate.HasCompiledArtifact )
			.OrderByDescending( candidate => candidate.HasVersionMarker )
			.ThenByDescending( candidate => candidate.LastWriteUtc )
			.ThenBy( candidate => candidate.AbsolutePath, StringComparer.OrdinalIgnoreCase )
			.Select( candidate => candidate.AbsolutePath )
			.FirstOrDefault() ?? "";

	private static bool SourceAssetExists( string relativePath )
	{
		if ( string.IsNullOrWhiteSpace( relativePath ) )
			return false;

		try
		{
			var absolute = Path.IsPathRooted( relativePath )
				? Path.GetFullPath( relativePath )
				: Path.GetFullPath( Path.Combine(
					GetContentRoot(),
					relativePath.TrimStart( '/', '\\' ) ) );
			return File.Exists( absolute ) || File.Exists( absolute + "_c" );
		}
		catch
		{
			return false;
		}
	}

	internal static string MaterialCandidateWrapperPath(
		string cacheRoot,
		string sourceHash,
		IEnumerable<SourceMaterialBinding> materials ) =>
		Path.Combine(
			WeaponMaterialPipeline.PreviewRevisionRoot( cacheRoot, materials ),
			"models",
			$"source_{sourceHash[..12]}_textured.vmdl" );

	public static void CleanupLegacyMaterialPreview(
		WeaponAnimationDocument document )
	{
		var cacheRoot = GetPreviewCacheRoot( document.DocumentId );
		var legacyDirectories = new[]
		{
			Path.Combine( cacheRoot, "materials" ),
			Path.Combine( cacheRoot, "texture-definitions" ),
			Path.Combine( cacheRoot, "source-textures" )
		};
		var staleFiles = legacyDirectories
			.Where( Directory.Exists )
			.SelectMany( directory => Directory.EnumerateFiles(
				directory,
				"*",
				SearchOption.AllDirectories ) )
			.ToList();

		if ( staleFiles.Count > 0 )
		{
			AssetGenerationService.DeleteGeneratedFiles( staleFiles );
			foreach ( var directory in legacyDirectories
				.Where( Directory.Exists )
				.OrderByDescending( directory => directory.Length ) )
			{
				try
				{
					Directory.Delete( directory, true );
				}
				catch ( Exception ex )
				{
					Log.Warning(
						$"[Weapon Animator] could not remove legacy preview material folder "
						+ $"'{directory}': {ex.Message}" );
				}
			}
		}
		CleanupUnversionedLegalPreviews( document, staleFiles );
		if ( staleFiles.Count > 0 )
		{
			Log.Info(
				$"[Weapon Animator] removed {staleFiles.Count} obsolete preview material source(s)." );
		}
	}

	private static void CleanupUnversionedLegalPreviews(
		WeaponAnimationDocument document,
		List<string> removedFiles )
	{
		var previewRoot = Path.Combine(
			GetContentRoot(),
			"weaponanim_preview_cache",
			document.DocumentId.ToString( "N" ) );
		if ( !Directory.Exists( previewRoot ) )
			return;

		var protectedModel = string.IsNullOrWhiteSpace(
			document.Source.CompiledModelPath )
				? ""
				: Path.GetFullPath( Path.Combine(
					GetContentRoot(),
					document.Source.CompiledModelPath.Replace(
						'/',
						Path.DirectorySeparatorChar ) ) );
		foreach ( var revisionDirectory in Directory.EnumerateDirectories( previewRoot )
			.Where( directory => !Path.GetFileName( directory ).Equals(
				"source-textures",
				StringComparison.OrdinalIgnoreCase ) ) )
		{
			var marker = Path.Combine(
				revisionDirectory,
				".weaponanim-preview-version" );
			var isProtected = !string.IsNullOrWhiteSpace( protectedModel )
				&& protectedModel.StartsWith(
					Path.GetFullPath( revisionDirectory )
						+ Path.DirectorySeparatorChar,
					StringComparison.OrdinalIgnoreCase );
			if ( isProtected || File.Exists( marker ) )
				continue;

			var revisionFiles = Directory.EnumerateFiles(
				revisionDirectory,
				"*",
				SearchOption.AllDirectories )
				.ToArray();
			AssetGenerationService.DeleteGeneratedFiles( revisionFiles );
			removedFiles.AddRange( revisionFiles );
			try
			{
				Directory.Delete( revisionDirectory, true );
			}
			catch ( Exception ex )
			{
				Log.Warning(
					$"[Weapon Animator] could not remove obsolete preview revision "
					+ $"'{revisionDirectory}': {ex.Message}" );
			}
		}
	}

	private static async Task<Model?> ReloadMaterialPreviewAsync(
		string wrapperAbsolute,
		string wrapperPath )
	{
		var deadline = DateTime.UtcNow.AddSeconds( 20 );
		var nextProgressLog = DateTime.UtcNow.AddSeconds( 2 );
		Model? model = null;
		while ( DateTime.UtcNow <= deadline )
		{
			await Task.Delay( 50 );
			var asset = AssetSystem.FindByPath( wrapperAbsolute );
			try
			{
				model = asset?.LoadResource<Model>() ?? Model.Load( wrapperPath );
			}
			catch ( Exception ex )
			{
				Log.Warning(
					$"[Weapon Animator] preview material model reload attempt threw: {ex.Message}" );
				model = null;
			}

			if ( model is not null && !model.IsError && model.BoneCount > 0 )
				return model;

			if ( DateTime.UtcNow >= nextProgressLog )
			{
				Log.Info(
					$"[Weapon Animator] waiting for material preview '{wrapperPath}': "
					+ $"bones={model?.BoneCount ?? 0}, "
					+ $"error={model?.IsError.ToString() ?? "not loaded"}, "
					+ $"assetPresent={asset is not null}, "
					+ $"compiledArtifact={File.Exists( wrapperAbsolute + "_c" )}." );
				nextProgressLog = DateTime.UtcNow.AddSeconds( 2 );
			}
		}

		return model;
	}

	private static string ImportMessage(
		Model model,
		string selectedPath,
		IReadOnlyCollection<SourceMaterialBinding> materials )
	{
		var textured = materials.Count( material => material.HasUsableTextures );
		var materialSummary = materials.Count == 0
			? " No source material slots were reported."
			: $" Matched textures for {textured} of {materials.Count} material slot(s).";
		return $"Imported {model.BoneCount} bones from {Path.GetFileName( selectedPath )}."
			+ materialSummary;
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
