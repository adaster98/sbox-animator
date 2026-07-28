#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Editor;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public sealed class GenerationResult
{
	public bool Success { get; init; }
	public string OutputFolder { get; init; } = "";
	public ValidationReport Validation { get; init; } = new();
	public List<GenerationDiagnostic> Diagnostics { get; init; } = [];
	public List<string> GeneratedFiles { get; init; } = [];
}

public sealed class AssetGenerationService
{
	public const string GeneratorVersion = "1.9.0";
	private const string ManifestFile = "weaponanim.manifest.json";

	public async Task<GenerationResult> GenerateAsync( WeaponAnimationDocument document )
	{
		var validation = WeaponAnimationValidator.ValidateForGeneration( document );
		if ( !validation.IsValid )
			return Failed( validation, "generation.validation", "Generation is blocked by validation errors." );

		string outputRoot;
		string relativeRoot;
		try
		{
			outputRoot = ResolveOutputRoot( document );
			relativeRoot = WeaponSourceImporter.RelativeAssetPath( outputRoot );
		}
		catch ( Exception ex )
		{
			Log.Error( $"[Weapon Animator] output path resolution failed: {ex}" );
			return Failed(
				validation,
				"generation.output",
				$"Could not prepare the generated output folder: {ex.Message}" );
		}

		try
		{
			LoadOwnershipManifest( document, outputRoot );
		}
		catch ( Exception ex )
		{
			Log.Error( $"[Weapon Animator] could not load the ownership manifest: {ex}" );
			return Failed(
				validation,
				"ownership.manifest",
				$"Could not read the generated ownership manifest: {ex.Message}" );
		}

		var diagnostics = new List<GenerationDiagnostic>();
		HostSkeleton skeleton;
		Dictionary<string, string> files;
		try
		{
			skeleton = HostSkeletonBuilder.Build( document );
			files = BuildFiles( document, skeleton, relativeRoot );
		}
		catch ( Exception ex )
		{
			Log.Error( $"[Weapon Animator] could not assemble generated sources: {ex}" );
			return Failed(
				validation,
				"generation.sources",
				$"Could not assemble generated sources: {ex.Message}" );
		}
		var previousFiles = document.Manifest.Files
			.Select( x => x.RelativePath )
			.ToHashSet( StringComparer.OrdinalIgnoreCase );

		foreach ( var file in files.Keys )
		{
			var absolute = Path.Combine( outputRoot, file );
			if ( File.Exists( absolute ) && !previousFiles.Contains( file ) )
			{
				diagnostics.Add( Diagnostic(
					ValidationSeverity.Error,
					"ownership.conflict",
					$"Refusing to replace unowned file '{file}'.",
					absolute ) );
			}
		}

		if ( diagnostics.Any( x => x.Severity == ValidationSeverity.Error ) )
			return new GenerationResult
			{
				Success = false,
				OutputFolder = outputRoot,
				Validation = validation,
				Diagnostics = diagnostics
			};

		// Generation compiles straight into the output folder. A throwaway staging copy is not
		// safe here: once ModelDoc compiles a .vmdl the asset database records its .dmx
		// dependencies, and deleting those sources afterwards leaves the asset permanently
		// out of date, which the engine then retries every frame for the rest of the session.
		// The backup and rollback below already restore owned outputs when a compile fails.
		DiscardAbandonedStage( document );

		var backups = new Dictionary<string, byte[]>( StringComparer.OrdinalIgnoreCase );
		var newFiles = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		try
		{
			Directory.CreateDirectory( outputRoot );
			PrepareCompiledConsumersForRewrite(
				outputRoot,
				files.Keys,
				backups,
				newFiles );
			WriteFiles( outputRoot, files, backups, newFiles, previousFiles );
			VerifyGeneratedSources( outputRoot, files.Keys );
			RegisterGeneratedDependencies( outputRoot, files.Keys );

			await CompileAndInspectAsync( document, outputRoot, relativeRoot, skeleton, diagnostics );
			if ( diagnostics.Any( x => x.Severity == ValidationSeverity.Error ) )
				throw new InvalidOperationException( "One or more generated assets failed to compile or reload." );

			RemoveObsoleteOwnedFiles( document, outputRoot, files.Keys, diagnostics );
			var inputHash = InputHash( document );
			var generatedUtc = document.Manifest.InputHash == inputHash
				? document.Manifest.GeneratedUtc
				: DateTime.UtcNow;
			if ( generatedUtc == default )
				generatedUtc = DateTime.UtcNow;

			var manifest = new GenerationManifest
			{
				GeneratorVersion = GeneratorVersion,
				GeneratedUtc = generatedUtc,
				InputHash = inputHash,
				Diagnostics = diagnostics,
				Files = files
					.OrderBy( x => x.Key )
					.Select( x => new GeneratedFileRecord
					{
						RelativePath = x.Key.Replace( '\\', '/' ),
						Sha256 = HashText( x.Value ),
						Kind = Path.GetExtension( x.Key ).TrimStart( '.' )
					} )
					.ToList()
			};

			var manifestPath = Path.Combine( outputRoot, ManifestFile );
			if ( File.Exists( manifestPath ) )
				backups.TryAdd( manifestPath, File.ReadAllBytes( manifestPath ) );
			else
				newFiles.Add( manifestPath );
			AtomicFile.WriteAllText( manifestPath, Json.Serialize( manifest ) );
			document.Manifest = manifest;

			return new GenerationResult
			{
				Success = true,
				OutputFolder = outputRoot,
				Validation = validation,
				Diagnostics = diagnostics,
				GeneratedFiles = files.Keys
					.Append( ManifestFile )
					.OrderBy( x => x )
					.ToList()
			};
		}
		catch ( Exception ex )
		{
			Log.Error( $"[Weapon Animator] generation rolled back: {ex}" );
			DeleteGeneratedFiles( newFiles );
			foreach ( var backup in backups )
				File.WriteAllBytes( backup.Key, backup.Value );

			diagnostics.Add( Diagnostic(
				ValidationSeverity.Error,
				"generation.rollback",
				$"Generation failed and owned outputs were restored: {ex.Message}" ) );
			return new GenerationResult
			{
				Success = false,
				OutputFolder = outputRoot,
				Validation = validation,
				Diagnostics = diagnostics
			};
		}
	}

	private static void WriteFiles(
		string root,
		IReadOnlyDictionary<string, string> files,
		Dictionary<string, byte[]>? backups = null,
		HashSet<string>? newFiles = null,
		IReadOnlySet<string>? previouslyOwnedFiles = null )
	{
		Directory.CreateDirectory( root );

		// Sources before the .vmdl/.vanmgrph/.prefab that consume them, so the asset system never
		// sees a model whose animation files have not landed yet.
		foreach ( var name in OrderForWrite( files.Keys ) )
		{
			var absolute = Path.Combine( root, name );
			if ( backups is not null && File.Exists( absolute ) )
				backups[absolute] = File.ReadAllBytes( absolute );
			else if ( newFiles is not null
				&& !File.Exists( absolute )
				&& backups?.ContainsKey( absolute ) != true )
			{
				if ( ShouldDeleteCreatedFileOnRollback(
					name,
					previouslyOwnedFiles?.Contains( name ) == true ) )
					newFiles.Add( absolute );
			}

			// Deliberately not an atomic write-and-rename. Replacing the file makes the engine's
			// directory watcher report it as removed and re-added, and a dependency sampled during
			// that gap is cached as "file stopped existing" — which recompiles the model forever.
			// Truncating in place only ever looks like a modification.
			File.WriteAllText( absolute, files[name], new UTF8Encoding( false ) );
		}
	}

	internal static IEnumerable<string> OrderForWrite( IEnumerable<string> paths ) =>
		paths
			.OrderBy( ConsumerWriteRank )
			.ThenBy( path => path, StringComparer.OrdinalIgnoreCase );

	private static int ConsumerWriteRank( string path )
	{
		var extension = Path.GetExtension( path ).ToLowerInvariant();
		if ( !IsCompiledConsumer( path ) )
			return 0;
		if ( IsBootstrapHost( path ) )
			return 1;
		return extension switch
		{
			".vanmgrph" => 2,
			".vmdl" => 3,
			".prefab" => 4,
			_ => 5
		};
	}

	/// <summary>
	/// Extensions the asset system compiles and then tracks dependencies for. Removing one of
	/// these before the sources it consumes keeps the engine from retrying a compile against
	/// files that are about to disappear.
	/// </summary>
	private static readonly string[] CompiledConsumerExtensions =
		[".vmdl", ".vanmgrph", ".prefab"];

	private static bool IsCompiledConsumer( string path ) =>
		CompiledConsumerExtensions.Contains(
			Path.GetExtension( path ),
			StringComparer.OrdinalIgnoreCase );

	internal static bool ShouldDeleteCreatedFileOnRollback(
		string relativePath,
		bool previouslyOwned ) =>
		!previouslyOwned
		|| IsCompiledConsumer( relativePath );

	private static int ConsumerRemovalRank( string path ) =>
		IsBootstrapHost( path )
			? 1
			: Path.GetExtension( path ).ToLowerInvariant() switch
		{
			".prefab" => 4,
			".vmdl" => 3,
			".vanmgrph" => 2,
			_ => 0
		};

	private static bool IsBootstrapHost( string path ) =>
		path.EndsWith( "_host_bootstrap.vmdl", StringComparison.OrdinalIgnoreCase )
		|| path.EndsWith( "_vm_bootstrap.vmdl", StringComparison.OrdinalIgnoreCase );

	internal static IEnumerable<string> OrderForRemoval( IEnumerable<string> absolutePaths ) =>
		absolutePaths
			.OrderByDescending( ConsumerRemovalRank )
			.ThenBy( path => path, StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Drop compiled consumers before rewriting their source set. This clears dependency
	/// metadata inherited from older generators without ever removing a DMX dependency.
	/// </summary>
	private static void PrepareCompiledConsumersForRewrite(
		string outputRoot,
		IEnumerable<string> relativePaths,
		Dictionary<string, byte[]> backups,
		HashSet<string> newFiles )
	{
		var consumers = relativePaths
			.Where( IsCompiledConsumer )
			.Select( path => Path.Combine( outputRoot, path ) )
			.ToArray();
		foreach ( var absolute in OrderForRemoval( consumers ) )
		{
			if ( File.Exists( absolute ) )
				backups.TryAdd( absolute, File.ReadAllBytes( absolute ) );
			else
				newFiles.Add( absolute );

			var registered = AssetSystem.FindByPath( absolute );
			if ( registered is not null )
			{
				Log.Info(
					$"[Weapon Animator] resetting registered consumer '{registered.Path}' "
					+ "before generation." );
				registered.Delete();
			}

			// Asset.Delete normally removes both files. Explicit cleanup also handles a stale
			// registry entry whose source path has already disappeared.
			foreach ( var path in new[] { absolute, absolute + "_c" } )
			{
				if ( File.Exists( path ) )
					File.Delete( path );
			}
		}
	}

	private static void VerifyGeneratedSources(
		string outputRoot,
		IEnumerable<string> relativePaths )
	{
		var missing = relativePaths
			.Where( path => !File.Exists( Path.Combine( outputRoot, path ) ) )
			.ToArray();
		if ( missing.Length > 0 )
		{
			throw new IOException(
				$"Generated source set is incomplete: {string.Join( ", ", missing )}." );
		}

		Log.Info(
			$"[Weapon Animator] verified {relativePaths.Count()} persistent generation sources "
			+ $"under '{outputRoot}'." );
	}

	private static void RegisterGeneratedDependencies(
		string outputRoot,
		IEnumerable<string> relativePaths )
	{
		var sources = relativePaths
			.Where( path => !IsCompiledConsumer( path ) )
			.Select( path => Path.Combine( outputRoot, path ) )
			.ToArray();
		foreach ( var absolute in sources )
		{
			var asset = AssetSystem.RegisterFile( absolute );
			if ( asset is null || asset.IsDeleted || !asset.HasSourceFile )
			{
				throw new IOException(
					$"The asset system did not retain generated dependency '{absolute}'." );
			}
		}

		Log.Info(
			$"[Weapon Animator] registered {sources.Length} persistent source dependencies "
			+ "before compiling their consumers." );
	}

	internal static void DeleteGeneratedFiles( IEnumerable<string> absolutePaths )
	{
		foreach ( var path in OrderForRemoval( absolutePaths ).ToList() )
		{
			var deletedByAssetSystem = false;
			try
			{
				var asset = AssetSystem.FindByPath( path );
				if ( asset is not null )
				{
					Log.Info(
						$"[Weapon Animator] unregistering generated asset '{asset.Path}' "
						+ $"before removing '{path}'." );
					asset.Delete();
					deletedByAssetSystem = true;
				}
			}
			catch ( Exception ex )
			{
				Log.Warning(
					$"[Weapon Animator] asset-aware removal failed for '{path}': {ex.Message}" );
			}

			// Uncompiled DMX sources and verification runs have no Asset entry.
			foreach ( var target in new[] { path, path + "_c" } )
			{
				try
				{
					if ( File.Exists( target ) )
						File.Delete( target );
				}
				catch ( Exception ex )
				{
					Log.Warning( $"[Weapon Animator] could not remove '{target}': {ex.Message}" );
				}
			}

			if ( deletedByAssetSystem )
				Log.Info( $"[Weapon Animator] asset registry removal completed for '{path}'." );
		}
	}

	/// <summary>
	/// Generator versions before 1.3.0 compiled into a staging folder and then deleted it,
	/// which left the asset system recompiling assets whose sources were gone. Clear anything
	/// those runs left behind, dependants first.
	/// </summary>
	private static void DiscardAbandonedStage( WeaponAnimationDocument document )
	{
		try
		{
			var stageRoot = Path.Combine(
				WeaponSourceImporter.GetPreviewCacheRoot( document.DocumentId ),
				"generation-stage" );
			var slug = WeaponAnimationDocument.Slugify( document.Output.AssetName );
			var stalePaths = new HashSet<string>( StringComparer.OrdinalIgnoreCase )
			{
				Path.Combine( stageRoot, $"{slug}_host.vmdl" ),
				Path.Combine( stageRoot, $"{slug}_vm.vmdl" ),
				Path.Combine( stageRoot, $"{slug}_host_bootstrap.vmdl" ),
				Path.Combine( stageRoot, $"{slug}_vm_bootstrap.vmdl" ),
				Path.Combine( stageRoot, $"{slug}_source.vmdl" ),
				Path.Combine( stageRoot, $"{slug}.vanmgrph" ),
				Path.Combine( stageRoot, $"v_{slug}.prefab" ),
				Path.Combine( stageRoot, $"{slug}_host_reference.dmx" )
			};
			foreach ( var clip in document.Clips )
			{
				var stem = $"{slug}_{WeaponAnimationNames.SequenceName( clip )}";
				stalePaths.Add( Path.Combine( stageRoot, $"{stem}.smd" ) );
				stalePaths.Add( Path.Combine( stageRoot, $"{stem}.dmx" ) );
			}
			var normalizedStageRoot = Path.GetFullPath( stageRoot )
				.TrimEnd( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar )
				+ Path.DirectorySeparatorChar;
			var registeredStagePaths = AssetSystem.All
				.Where( asset => !string.IsNullOrWhiteSpace( asset.AbsolutePath ) )
				.Select( asset => Path.GetFullPath( asset.AbsolutePath ) )
				.Where( path => path.StartsWith(
					normalizedStageRoot,
					StringComparison.OrdinalIgnoreCase ) )
				.ToArray();
			stalePaths.UnionWith( registeredStagePaths );
			var stageExisted = Directory.Exists( stageRoot );
			if ( Directory.Exists( stageRoot ) )
			{
				stalePaths.UnionWith(
					Directory.GetFiles(
						stageRoot,
						"*",
						SearchOption.AllDirectories ) );
			}

			// Exact legacy paths are included even when their source files are already gone. This
			// lets Asset.Delete clear the stale registry entries that trigger on-demand retries.
			DeleteGeneratedFiles( stalePaths );
			if ( Directory.Exists( stageRoot ) )
				Directory.Delete( stageRoot, true );
			if ( stageExisted || registeredStagePaths.Length > 0 )
				Log.Info( $"[Weapon Animator] removed abandoned generation stage '{stageRoot}'." );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[Weapon Animator] could not clear the abandoned generation stage: {ex.Message}" );
		}
	}

	private static void RemoveObsoleteOwnedFiles(
		WeaponAnimationDocument document,
		string outputRoot,
		IEnumerable<string> generatedFiles,
		List<GenerationDiagnostic> diagnostics )
	{
		var retained = generatedFiles.ToHashSet( StringComparer.OrdinalIgnoreCase );
		var obsolete = document.Manifest.Files
			.Select( x => x.RelativePath )
			.Where( x => !retained.Contains( x ) )
			.Select( x => Path.Combine(
				outputRoot,
				x.Replace( '/', Path.DirectorySeparatorChar ) ) )
			.ToArray();
		if ( obsolete.Length == 0 )
			return;

		DeleteGeneratedFiles( obsolete );
		foreach ( var path in obsolete )
		{
			diagnostics.Add( Diagnostic(
				ValidationSeverity.Info,
				"ownership.obsolete_removed",
				$"Removed obsolete generated file '{Path.GetFileName( path )}'.",
				path ) );
		}
	}

	public static string GetOutputFolder( WeaponAnimationDocument document ) =>
		ResolveOutputRoot( document );

	internal static Dictionary<string, string> BuildFiles(
		WeaponAnimationDocument document,
		HostSkeleton skeleton,
		string relativeRoot )
	{
		var slug = WeaponAnimationDocument.Slugify( document.Output.AssetName );
		var files = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		var referenceName = $"{slug}_host_reference.dmx";
		var bootstrapHostName = $"{slug}_vm_bootstrap.vmdl";
		var hostName = $"{slug}_vm.vmdl";
		var graphName = $"{slug}.vanmgrph";
		var prefabName = $"v_{slug}.prefab";
		files[referenceName] = DmxWriter.WriteReference( skeleton );

		var clipSources = new List<(WeaponAnimationClip Clip, string Source)>();
		foreach ( var clip in document.Clips.OrderBy( x => x.Name ) )
		{
			var clipName =
				$"{slug}_sequence_{WeaponAnimationNames.SequenceName( clip )}.dmx";
			files[clipName] = DmxWriter.WriteAnimation( document, skeleton, clip );
			clipSources.Add( (clip, $"{relativeRoot}/{clipName}") );
		}

		var graphPath = document.Output.GenerateGraph && document.Graph.GenerateGraph
			? $"{relativeRoot}/{graphName}"
			: "";
		var placement = WeaponAnimationMath.Compose(
			document.Calibration.PhysicalTransform,
			document.Calibration.FramingTransform );
		var weaponMesh = new HostWeaponMesh(
			ResolveEmbeddableSourcePath( document ),
			document.Source.SourceRootBoneName,
			placement,
			ExcludedBranchRoots( document ).ToArray() );
		var hostSource = ModelDocWriter.WriteHost(
			$"{relativeRoot}/{referenceName}",
			clipSources,
			graphPath,
			skeleton.Bones.Select( bone => bone.Name ),
			weaponMesh,
			BuildHostAttachments( document, skeleton ) );
		files[hostName] = hostSource;

		if ( document.Output.GenerateGraph && document.Graph.GenerateGraph )
		{
			// The graph previews a permanent graph-free sibling, breaking the otherwise circular
			// host -> graph -> preview-host compile dependency.
			files[bootstrapHostName] = ModelDocWriter.WriteHost(
				$"{relativeRoot}/{referenceName}",
				clipSources,
				"",
				skeleton.Bones.Select( bone => bone.Name ),
				weaponMesh,
				BuildHostAttachments( document, skeleton ) );
			files[graphName] = AnimGraphWriter.Write(
				document,
				$"{relativeRoot}/{bootstrapHostName}" );
		}

		if ( document.Output.GeneratePrefab )
			files[prefabName] = PrefabWriter.Write(
				document,
				$"{relativeRoot}/{hostName}" );

		return files;
	}

	private static string ResolveEmbeddableSourcePath( WeaponAnimationDocument document )
	{
		var extension = Path.GetExtension( document.Source.SourcePath );
		if ( extension.Equals( ".fbx", StringComparison.OrdinalIgnoreCase )
			|| extension.Equals( ".dmx", StringComparison.OrdinalIgnoreCase )
			|| extension.Equals( ".obj", StringComparison.OrdinalIgnoreCase ) )
			return document.Source.SourcePath;

		throw new InvalidOperationException(
			$"The standard single-renderer viewmodel currently needs an FBX, DMX, or OBJ render source; "
			+ $"'{extension}' cannot be embedded by ModelDoc." );
	}

	private static IEnumerable<HostAttachment> BuildHostAttachments(
		WeaponAnimationDocument document,
		HostSkeleton skeleton )
	{
		var sourceRoot = document.Rig.FindBone( document.Rig.SourceSkeletonRootId );
		var compilerModel = skeleton.BuildCompilerBindModelTransforms();
		var placement = WeaponAnimationMath.Compose(
			document.Calibration.PhysicalTransform,
			document.Calibration.FramingTransform );
		foreach ( var anchor in document.Calibration.Anchors.Where( x =>
			x.Kind is AnchorKind.Muzzle or AnchorKind.Eject or AnchorKind.Custom ) )
		{
			var parent = document.Rig.FindBone( anchor.BoneName ) ?? sourceRoot;
			if ( parent is null )
				continue;

			var parentName = parent.Id.Equals(
				document.Rig.SourceSkeletonRootId,
				StringComparison.OrdinalIgnoreCase )
					? "weapon_root"
					: parent.Name;
			if ( !compilerModel.TryGetValue( parentName, out var compiledParent ) )
				continue;

			var anchorModelPosition = placement.PointToWorld( anchor.LocalPosition );
			var anchorModelRotation = placement.Rotation * anchor.LocalRotation;
			yield return new HostAttachment(
				anchor.Kind switch
				{
					AnchorKind.Muzzle => "muzzle",
					AnchorKind.Eject => "eject",
					_ => WeaponAnimationDocument.Slugify( anchor.Name )
				},
				parentName,
				compiledParent.PointToLocal( anchorModelPosition ),
				compiledParent.Rotation.Inverse * anchorModelRotation );
		}
	}

	private static IEnumerable<string> ExcludedBranchRoots(
		WeaponAnimationDocument document )
	{
		foreach ( var bone in document.Rig.Bones.Where( x =>
			x.Inclusion == WeaponBoneInclusion.Excluded ) )
		{
			var parent = document.Rig.FindBone( bone.ParentId );
			if ( parent is null || parent.Inclusion != WeaponBoneInclusion.Excluded )
				yield return string.IsNullOrWhiteSpace( bone.OriginalName )
					? bone.Name
					: bone.OriginalName;
		}
	}

	/// <summary>
	/// Seconds to let a single generated asset finish compiling before treating it as failed.
	/// </summary>
	private const float CompileTimeoutSeconds = 120.0f;
	private const float HostReloadTimeoutSeconds = 20.0f;

	/// <summary>
	/// <see cref="Asset.Compile"/> only queues the work; the resource compiler finishes on later
	/// frames. Sampling <see cref="Asset.IsCompiled"/> straight afterwards reports a false
	/// failure, so wait for the asset system to settle on a real verdict.
	/// </summary>
	private static async Task<bool> WaitForCompileAsync(
		Asset asset,
		string sourceAbsolute )
	{
		var queued = asset.Compile( true );
		Log.Info(
			$"[Weapon Animator] compile request for '{asset.Path}': queued={queued}, "
			+ $"deleted={asset.IsDeleted}, canRecompile={asset.CanRecompile}, "
			+ $"hasSource={asset.HasSourceFile}." );

		var deadline = DateTime.UtcNow.AddSeconds( CompileTimeoutSeconds );
		var nextProgressLog = DateTime.UtcNow.AddSeconds( 5 );
		var retriedLiveAsset = false;
		while ( true )
		{
			// Asset.Delete followed by RegisterFile can leave callers holding the retired managed
			// wrapper while the directory watcher has already created and compiled a replacement.
			var current = AssetSystem.FindByPath( sourceAbsolute ) ?? asset;
			var compiledAbsolute = FreshCompiledArtifact( current, sourceAbsolute );
			if ( !string.IsNullOrWhiteSpace( compiledAbsolute ) )
			{
				if ( !current.IsCompiledAndUpToDate || !current.HasCompiledFile )
				{
					Log.Info(
						$"[Weapon Animator] accepted fresh compiled artifact '{compiledAbsolute}' "
						+ $"while the managed asset flags for '{current.Path}' caught up." );
				}
				return true;
			}

			if ( current.IsCompileFailed )
				return false;
			if ( current.IsCompiledAndUpToDate && current.HasCompiledFile )
			{
				Log.Error(
					$"[Weapon Animator] '{current.Path}' reports compiled but no fresh artifact "
					+ $"exists for '{sourceAbsolute}'." );
				return false;
			}

			if ( !retriedLiveAsset
				&& !ReferenceEquals( current, asset )
				&& current.CanRecompile )
			{
				retriedLiveAsset = true;
				var liveQueued = current.Compile( true );
				Log.Info(
					$"[Weapon Animator] retried compile through the live asset entry "
					+ $"'{current.Path}': queued={liveQueued}." );
			}
			if ( DateTime.UtcNow > deadline )
			{
				Log.Warning(
					$"[Weapon Animator] '{current.Path}' did not finish compiling within "
					+ $"{CompileTimeoutSeconds:0} seconds." );
				return false;
			}
			if ( DateTime.UtcNow >= nextProgressLog )
			{
				Log.Info(
					$"[Weapon Animator] waiting for '{current.Path}': "
					+ $"compiled={current.IsCompiled}, upToDate={current.IsCompiledAndUpToDate}, "
					+ $"hasCompiledFile={current.HasCompiledFile}, deleted={current.IsDeleted}, "
					+ $"canRecompile={current.CanRecompile}, hasSource={current.HasSourceFile}, "
					+ $"sourceExists={File.Exists( sourceAbsolute )}." );
				nextProgressLog = DateTime.UtcNow.AddSeconds( 5 );
			}

			await Task.Delay( 16 );
		}
	}

	private static string FreshCompiledArtifact(
		Asset asset,
		string sourceAbsolute )
	{
		var candidates = new HashSet<string>( StringComparer.OrdinalIgnoreCase )
		{
			sourceAbsolute + "_c"
		};
		try
		{
			var reported = asset.GetCompiledFile( true );
			if ( !string.IsNullOrWhiteSpace( reported ) )
				candidates.Add( reported );
		}
		catch
		{
			// A retired asset wrapper can throw while its replacement is registered.
		}

		return candidates.FirstOrDefault( compiled =>
			IsFreshCompiledArtifact( sourceAbsolute, compiled ) ) ?? "";
	}

	internal static bool IsFreshCompiledArtifact(
		string sourceAbsolute,
		string compiledAbsolute )
	{
		if ( !File.Exists( sourceAbsolute ) || !File.Exists( compiledAbsolute ) )
			return false;

		// Wine and the mounted filesystem can round source/compiled timestamps differently.
		return File.GetLastWriteTimeUtc( compiledAbsolute )
			>= File.GetLastWriteTimeUtc( sourceAbsolute ).AddSeconds( -2 );
	}

	private static async Task CompileAndInspectAsync(
		WeaponAnimationDocument document,
		string outputRoot,
		string relativeRoot,
		HostSkeleton skeleton,
		List<GenerationDiagnostic> diagnostics )
	{
		var slug = WeaponAnimationDocument.Slugify( document.Output.AssetName );
		var bootstrapHostFile = $"{slug}_vm_bootstrap.vmdl";
		var hostFile = $"{slug}_vm.vmdl";
		var graphEnabled = document.Output.GenerateGraph && document.Graph.GenerateGraph;
		var hostAbsolute = Path.Combine( outputRoot, hostFile );

		async Task<bool> Compile( string file, string? description = null )
		{
			var absolute = Path.Combine( outputRoot, file );
			var asset = AssetSystem.RegisterFile( absolute );
			if ( asset is null )
			{
				Log.Error( $"[Weapon Animator] could not register '{absolute}' as an asset." );
				diagnostics.Add( Diagnostic(
					ValidationSeverity.Error,
					"compile.failed",
					$"Could not register '{file}' with the asset system.",
					absolute ) );
				return false;
			}

			if ( await WaitForCompileAsync( asset, absolute ) )
			{
				diagnostics.Add( Diagnostic(
					ValidationSeverity.Info,
					"compile.ok",
					$"Compiled '{description ?? file}'.",
					asset.Path ) );
				return true;
			}

			Log.Error( $"[Weapon Animator] failed to compile '{absolute}'." );
			diagnostics.Add( Diagnostic(
				ValidationSeverity.Error,
				"compile.failed",
				$"Failed to compile '{description ?? file}'.",
				absolute ) );
			return false;
		}

		var hostCompiled = false;
		if ( graphEnabled )
		{
			var graphPath = $"{relativeRoot}/{slug}.vanmgrph";
			var linkedGraph = $"anim_graph_name = \"{graphPath}\"";
			var finalHostSource = File.ReadAllText( hostAbsolute );
			if ( !finalHostSource.Contains( linkedGraph, StringComparison.Ordinal ) )
			{
				diagnostics.Add( Diagnostic(
					ValidationSeverity.Error,
					"compile.graph_link",
					"The final host source does not contain its generated AnimGraph link.",
					hostAbsolute ) );
				return;
			}

			var bootstrapCompiled = await Compile(
				bootstrapHostFile,
				$"{bootstrapHostFile} graph preview" );
			var graphCompiled = await Compile( $"{slug}.vanmgrph" );
			if ( bootstrapCompiled && graphCompiled )
				hostCompiled = await Compile( hostFile, $"{hostFile} with AnimGraph" );
		}
		else
		{
			hostCompiled = await Compile( hostFile );
		}
		if ( !hostCompiled )
			return;

		var hostPath = $"{relativeRoot}/{slug}_vm.vmdl";
		var host = await ReloadGeneratedHostAsync(
			hostAbsolute,
			hostPath,
			skeleton );
		var missingRequiredBones = host is null || host.IsError
			? skeleton.Bones.Select( bone => bone.Name ).ToArray()
			: MissingRequiredBones( skeleton, host );
		if ( host is null || host.IsError || missingRequiredBones.Length > 0 )
		{
			Log.Error(
				$"[Weapon Animator] host '{hostPath}' reloaded with {host?.BoneCount ?? 0} bones "
				+ $"(required {skeleton.Bones.Count}, missing {missingRequiredBones.Length}, "
				+ $"error: {host?.IsError.ToString() ?? "not loaded"})." );
			diagnostics.Add( Diagnostic(
				ValidationSeverity.Error,
				"inspect.host",
				$"Host reload is missing required bones: "
				+ $"{string.Join( ", ", missingRequiredBones.Take( 8 ) )}.",
				hostPath ) );
			return;
		}

		var additionalBones = AdditionalCompiledBones( skeleton, host );
		if ( additionalBones.Length > 0 )
		{
			Log.Info(
				$"[Weapon Animator] compiled host includes {additionalBones.Length} additional "
				+ $"source-mesh bone entries: {string.Join( ", ", additionalBones.Take( 16 ) )}." );
			diagnostics.Add( Diagnostic(
				ValidationSeverity.Warning,
				"inspect.additional_bones",
				$"The visible source mesh contributed {additionalBones.Length} additional compiled "
				+ "bone entries; all required animation-host bones are present.",
				hostPath ) );
		}

		var normalizedScaleBones = CountCompiledScaleNormalizations( skeleton, host );
		if ( normalizedScaleBones > 0 )
		{
			Log.Info(
				$"[Weapon Animator] compiler normalized bind scale on {normalizedScaleBones} "
				+ "bone(s); calibrated mesh scale remains baked into the generated model." );
			diagnostics.Add( Diagnostic(
				ValidationSeverity.Info,
				"inspect.bind_scale_normalized",
				$"ModelDoc normalized bind scale on {normalizedScaleBones} bone(s).",
				hostPath ) );
		}

		var bindIssues = InspectCompiledBindPose( skeleton, host );
		if ( bindIssues.Count > 0 )
		{
			foreach ( var issue in bindIssues.Take( 8 ) )
			{
				var expectedBone = skeleton.ByName[issue.BoneName];
				var actualBone = host.Bones.GetBone( issue.BoneName );
				Log.Error(
					$"[Weapon Animator] compiled bind mismatch '{issue.BoneName}': "
					+ $"expectedParent='{expectedBone.ParentName}', "
					+ $"actualParent='{actualBone?.Parent?.Name ?? ""}', "
					+ $"position={issue.PositionDelta:0.######}, "
					+ $"rotation={issue.RotationDelta:0.######}, "
					+ $"scale={issue.ScaleDelta:0.######}, "
					+ $"expected={DescribeTransform( issue.Expected )}, "
					+ $"actual={DescribeTransform( issue.Actual )}." );
			}
			diagnostics.Add( Diagnostic(
				ValidationSeverity.Error,
				"inspect.bind_pose",
				$"Compiled host bind pose differs from the authored host on {bindIssues.Count} "
				+ $"bone(s); first mismatch: {bindIssues[0].BoneName}.",
				hostPath ) );
			return;
		}

		LogRotatingWeaponPivotDiagnostics( document, skeleton, host, diagnostics, hostPath );

		var sequenceNames = Enumerable.Range( 0, host.AnimationCount )
			.Select( host.GetAnimationName )
			.Where( name => !string.IsNullOrWhiteSpace( name ) )
			.ToHashSet( StringComparer.OrdinalIgnoreCase );
		var expectedSequences = document.Clips
			.Select( WeaponAnimationNames.SequenceName )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.ToArray();
		var missingSequences = expectedSequences
			.Where( name => !sequenceNames.Contains( name ) )
			.ToArray();
		Log.Info(
			$"[Weapon Animator] host '{hostPath}' exposes {host.AnimationCount} animation "
			+ $"sequence(s): {string.Join( ", ", sequenceNames.OrderBy( x => x ) )}." );
		if ( missingSequences.Length > 0 )
		{
			diagnostics.Add( Diagnostic(
				ValidationSeverity.Error,
				"inspect.sequences",
				$"Compiled host is missing generated animation sequences: "
				+ $"{string.Join( ", ", missingSequences )}.",
				hostPath ) );
			return;
		}
		diagnostics.Add( Diagnostic(
			ValidationSeverity.Info,
			"inspect.sequences",
			$"Host exposes all {expectedSequences.Length} generated animation sequences.",
			hostPath ) );

		if ( graphEnabled )
		{
			var graph = host.AnimGraph;
			var requiredParameters = new[] { "b_attack", "b_reload", "b_empty" };
			var missing = graph is null || graph.IsError
				? requiredParameters
				: requiredParameters
					.Where( name => !graph.TryGetParameterIndex( name, out _ ) )
					.ToArray();
			if ( graph is null || graph.IsError || missing.Length > 0 )
			{
				Log.Error(
					$"[Weapon Animator] host '{hostPath}' has no usable AnimGraph parameters. "
					+ $"Graph loaded: {graph is not null}, graph error: {graph?.IsError.ToString() ?? "n/a"}, "
					+ $"missing: {string.Join( ", ", missing )}." );
				diagnostics.Add( Diagnostic(
					ValidationSeverity.Error,
					"inspect.animgraph",
					$"Host reload is missing AnimGraph parameters: {string.Join( ", ", missing )}.",
					hostPath ) );
			}
			else
			{
				diagnostics.Add( Diagnostic(
					ValidationSeverity.Info,
					"inspect.animgraph",
					$"Host exposes {graph.ParamCount} AnimGraph parameters, including the Facepunch firearm profile.",
					hostPath ) );
			}
		}

		// Compile the prefab only after its model and graph have reloaded successfully. This keeps
		// a transient model-cache delay from producing and then rolling back a dependent prefab.
		if ( document.Output.GeneratePrefab )
			await Compile( $"v_{slug}.prefab" );
	}

	private static async Task<Model?> ReloadGeneratedHostAsync(
		string hostAbsolute,
		string hostPath,
		HostSkeleton skeleton )
	{
		var expectedBoneCount = skeleton.Bones.Count;
		var started = DateTime.UtcNow;
		var deadline = started.AddSeconds( HostReloadTimeoutSeconds );
		var nextProgressLog = started.AddSeconds( 2 );
		Model? last = null;

		while ( DateTime.UtcNow <= deadline )
		{
			var asset = AssetSystem.FindByPath( hostAbsolute );
			// Successful compilation can precede resource hotload by several frames. Reacquire
			// both the Asset and Model until the replacement resource is visible.
			await Task.Delay( 50 );
			try
			{
				last = asset?.LoadResource<Model>() ?? Model.Load( hostPath );
			}
			catch ( Exception ex )
			{
				Log.Warning(
					$"[Weapon Animator] host reload attempt for '{hostPath}' threw: {ex.Message}" );
				last = null;
			}

			var missing = last is null || last.IsError
				? expectedBoneCount
				: MissingRequiredBones( skeleton, last ).Length;
			if ( last is not null && !last.IsError && missing == 0 )
			{
				var elapsed = (DateTime.UtcNow - started).TotalMilliseconds;
				Log.Info(
					$"[Weapon Animator] host '{hostPath}' reloaded with "
					+ $"{last.BoneCount} bones after {elapsed:0} ms." );
				return last;
			}

			if ( DateTime.UtcNow >= nextProgressLog )
			{
				Log.Info(
					$"[Weapon Animator] waiting for host resource '{hostPath}': "
					+ $"bones={last?.BoneCount ?? 0} (required {expectedBoneCount}, missing {missing}), "
					+ $"error={last?.IsError.ToString() ?? "not loaded"}, "
					+ $"assetPresent={asset is not null}, "
					+ $"compiledArtifact={File.Exists( hostAbsolute + "_c" )}." );
				nextProgressLog = DateTime.UtcNow.AddSeconds( 2 );
			}
		}

		return last;
	}

	private static string[] MissingRequiredBones(
		HostSkeleton skeleton,
		Model host ) =>
		skeleton.Bones
			.Where( expected => host.Bones.GetBone( expected.Name ) is null )
			.Select( expected => expected.Name )
			.ToArray();

	private static string[] AdditionalCompiledBones(
		HostSkeleton skeleton,
		Model host )
	{
		var expected = skeleton.Bones
			.Select( bone => bone.Name )
			.ToHashSet( StringComparer.OrdinalIgnoreCase );
		var additionalNames = host.Bones.AllBones
			.Where( bone => !expected.Contains( bone.Name ) )
			.Select( bone => bone.Name );
		var duplicateNames = host.Bones.AllBones
			.GroupBy( bone => bone.Name, StringComparer.OrdinalIgnoreCase )
			.Where( group => group.Count() > 1 )
			.Select( group => $"{group.Key} ×{group.Count()}" );
		return additionalNames
			.Concat( duplicateNames )
			.OrderBy( name => name, StringComparer.OrdinalIgnoreCase )
			.ToArray();
	}

	internal sealed record CompiledBindIssue(
		string BoneName,
		float PositionDelta,
		float RotationDelta,
		float ScaleDelta,
		Transform Expected,
		Transform Actual );

	private static IReadOnlyList<CompiledBindIssue> InspectCompiledBindPose(
		HostSkeleton skeleton,
		Model host,
		float positionTolerance = 0.02f,
		float rotationTolerance = 0.005f )
	{
		var issues = new List<CompiledBindIssue>();
		var compiledExpectation = skeleton.BuildCompilerBindModelTransforms();
		foreach ( var expected in skeleton.Bones )
		{
			var actualBone = host.Bones.GetBone( expected.Name );
			var expectedTransform = compiledExpectation[expected.Name];
			if ( actualBone is null )
			{
				issues.Add( new CompiledBindIssue(
					expected.Name,
					float.PositiveInfinity,
					float.PositiveInfinity,
					float.PositiveInfinity,
					expectedTransform,
					Transform.Zero ) );
				continue;
			}

			var actual = actualBone.LocalTransform;
			var positionDelta = expectedTransform.Position.Distance( actual.Position );
			var rotationDelta = MathF.Max(
				(expectedTransform.Rotation.Forward - actual.Rotation.Forward).Length,
				(expectedTransform.Rotation.Up - actual.Rotation.Up).Length );
			var scaleDelta = (expectedTransform.Scale - actual.Scale).Length;
			if ( positionDelta > positionTolerance
				|| rotationDelta > rotationTolerance )
			{
				issues.Add( new CompiledBindIssue(
					expected.Name,
					positionDelta,
					rotationDelta,
					scaleDelta,
					expectedTransform,
					actual ) );
			}
		}

		return issues;
	}

	private static int CountCompiledScaleNormalizations(
		HostSkeleton skeleton,
		Model host,
		float scaleTolerance = 0.005f ) =>
		skeleton.Bones.Count( expected =>
		{
			var actual = host.Bones.GetBone( expected.Name );
			return actual is not null
				&& (expected.BindModelTransform.Scale - actual.LocalTransform.Scale).Length
					> scaleTolerance;
		} );

	private static void LogRotatingWeaponPivotDiagnostics(
		WeaponAnimationDocument document,
		HostSkeleton skeleton,
		Model host,
		List<GenerationDiagnostic> diagnostics,
		string hostPath )
	{
		var compilerLocal = skeleton.BuildCompilerBindLocalTransforms();
		var targets = document.Clips
			.SelectMany( clip => clip.Tracks )
			.Where( track => skeleton.ByName.TryGetValue( track.Target, out var bone )
				&& bone.IsWeaponBone
				&& track.Keys.Any( key => RotationDiffers(
					key.Rotation,
					skeleton.GetBindLocal( bone ).Rotation ) ) )
			.Select( track => track.Target )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.OrderBy( name => name, StringComparer.OrdinalIgnoreCase )
			.ToArray();

		foreach ( var target in targets )
		{
			var expected = skeleton.ByName[target];
			var actual = host.Bones.GetBone( target );
			var actualParent = actual?.Parent;
			var actualLocal = actual is null
				? Transform.Zero
				: actualParent is null
					? actual.LocalTransform
					: actualParent.LocalTransform.ToLocal( actual.LocalTransform );
			Log.Info(
				$"[Weapon Animator] rotating weapon pivot '{target}': "
				+ $"parent expected='{expected.ParentName}', actual='{actualParent?.Name ?? ""}', "
				+ $"authoredLocal={DescribeTransform( skeleton.GetBindLocal( expected ) )}, "
				+ $"exportLocal={DescribeTransform( compilerLocal[target] )}, "
				+ $"compiledLocal={DescribeTransform( actualLocal )}, "
				+ $"compiledModel={DescribeTransform( actual?.LocalTransform ?? Transform.Zero )}." );
		}

		if ( targets.Length > 0 )
		{
			diagnostics.Add( Diagnostic(
				ValidationSeverity.Info,
				"inspect.rotation_pivots",
				$"Verified {targets.Length} rotation-driven weapon bone pivot(s) in compiled bind space.",
				hostPath ) );
		}
	}

	private static bool RotationDiffers(
		Rotation left,
		Rotation right,
		float tolerance = 0.001f ) =>
		(left.Forward - right.Forward).Length > tolerance
			|| (left.Up - right.Up).Length > tolerance;

	private static string DescribeTransform( Transform transform ) =>
		$"pos({transform.Position.x:0.####},{transform.Position.y:0.####},{transform.Position.z:0.####}) "
		+ $"rot({transform.Rotation.x:0.####},{transform.Rotation.y:0.####},"
		+ $"{transform.Rotation.z:0.####},{transform.Rotation.w:0.####}) "
		+ $"scale({transform.Scale.x:0.####},{transform.Scale.y:0.####},"
		+ $"{transform.Scale.z:0.####})";

	private static string ResolveOutputRoot( WeaponAnimationDocument document )
	{
		return ResolveOutputRootForContentRoot(
			document,
			WeaponSourceImporter.GetContentRoot() );
	}

	internal static string ResolveOutputRootForContentRoot(
		WeaponAnimationDocument document,
		string contentRoot )
	{
		if ( string.IsNullOrWhiteSpace( contentRoot ) )
			throw new InvalidOperationException( "The current project's Assets directory is unavailable." );

		document.Output ??= new OutputSettings
		{
			AssetName = WeaponAnimationDocument.Slugify( document.Name )
		};
		var configured = string.IsNullOrWhiteSpace( document.Output.OutputFolder )
			? document.Output.GetDefaultRelativeFolder()
			: document.Output.OutputFolder;
		configured = configured.Trim().Replace( '\\', '/' ).TrimStart( '/' );
		if ( string.IsNullOrWhiteSpace( configured ) )
			configured = document.Output.GetDefaultRelativeFolder();
		if ( configured.Length >= 2
			&& char.IsLetter( configured[0] )
			&& configured[1] == ':' )
		{
			throw new InvalidOperationException(
				"Generated output must use a path relative to the project's Assets folder." );
		}

		var assetsRoot = Path.GetFullPath( contentRoot );
		var full = Path.GetFullPath( Path.Combine(
			assetsRoot,
			configured.Replace( '/', Path.DirectorySeparatorChar ) ) );
		var relative = Path.GetRelativePath( assetsRoot, full );
		if ( Path.IsPathRooted( relative )
			|| relative.Equals( "..", StringComparison.Ordinal )
			|| relative.StartsWith( $"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal )
			|| relative.StartsWith( $"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal ) )
			throw new InvalidOperationException( "Generated output must stay inside the project's Assets folder." );
		return full;
	}

	private static void LoadOwnershipManifest(
		WeaponAnimationDocument document,
		string outputRoot )
	{
		var manifestPath = Path.Combine( outputRoot, ManifestFile );
		if ( !File.Exists( manifestPath ) )
		{
			document.Manifest ??= new GenerationManifest();
			return;
		}

		var manifest = Json.Deserialize<GenerationManifest>(
			File.ReadAllText( manifestPath ) );
		if ( manifest is null )
			throw new InvalidDataException( $"'{manifestPath}' does not contain a valid manifest." );

		document.Manifest = manifest;
	}

	private static string InputHash( WeaponAnimationDocument document )
	{
		var clone = Json.Deserialize<WeaponAnimationDocument>( Json.Serialize( document ) )
			?? throw new InvalidOperationException( "Could not clone the weapon animation document." );
		clone.Manifest = new GenerationManifest();
		clone.Workspace = new WorkspaceState();
		return HashText( Json.Serialize( clone ) );
	}

	private static string HashText( string value ) =>
		Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( value ) ) )
			.ToLowerInvariant();

	private static GenerationResult Failed(
		ValidationReport validation,
		string code,
		string message ) => new()
	{
		Success = false,
		Validation = validation,
		Diagnostics = [Diagnostic( ValidationSeverity.Error, code, message )]
	};

	private static GenerationDiagnostic Diagnostic(
		ValidationSeverity severity,
		string code,
		string message,
		string assetPath = "" ) => new()
	{
		Severity = severity,
		Code = code,
		Message = message,
		AssetPath = assetPath
	};
}
