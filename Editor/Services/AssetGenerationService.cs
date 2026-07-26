#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
	public const string GeneratorVersion = "1.1.0";
	private const string ManifestFile = "weaponanim.manifest.json";

	public GenerationResult Generate( WeaponAnimationDocument document )
	{
		var validation = WeaponAnimationValidator.ValidateForGeneration( document );
		if ( !validation.IsValid )
			return Failed( validation, "generation.validation", "Generation is blocked by validation errors." );

		var outputRoot = ResolveOutputRoot( document );
		var relativeRoot = WeaponSourceImporter.RelativeAssetPath( outputRoot );
		var skeleton = HostSkeletonBuilder.Build( document );
		var files = BuildFiles( document, skeleton, relativeRoot );
		var diagnostics = new List<GenerationDiagnostic>();
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

		var stageRoot = Path.Combine(
			WeaponSourceImporter.GetPreviewCacheRoot( document.DocumentId ),
			"generation-stage" );
		var stageRelativeRoot = WeaponSourceImporter.RelativeAssetPath( stageRoot );
		try
		{
			ResetStage( stageRoot );
			var stagedFiles = BuildFiles( document, skeleton, stageRelativeRoot );
			WriteFiles( stageRoot, stagedFiles );
			CompileAndInspect( document, stageRoot, stageRelativeRoot, skeleton, diagnostics );
			if ( diagnostics.Any( x => x.Severity == ValidationSeverity.Error ) )
			{
				return new GenerationResult
				{
					Success = false,
					OutputFolder = outputRoot,
					Validation = validation,
					Diagnostics = diagnostics
				};
			}
		}
		catch ( Exception ex )
		{
			Log.Error( $"[Weapon Animator] staged generation failed: {ex}" );
			diagnostics.Add( Diagnostic(
				ValidationSeverity.Error,
				"generation.stage",
				$"Staged generation failed before final files were changed: {ex.Message}",
				stageRoot ) );
			return new GenerationResult
			{
				Success = false,
				OutputFolder = outputRoot,
				Validation = validation,
				Diagnostics = diagnostics
			};
		}
		finally
		{
			TryDeleteStage( stageRoot );
		}

		var backups = new Dictionary<string, byte[]>( StringComparer.OrdinalIgnoreCase );
		var newFiles = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		try
		{
			Directory.CreateDirectory( outputRoot );
			WriteFiles( outputRoot, files, backups, newFiles );

			CompileAndInspect( document, outputRoot, relativeRoot, skeleton, diagnostics );
			if ( diagnostics.Any( x => x.Severity == ValidationSeverity.Error ) )
				throw new InvalidOperationException( "One or more generated assets failed to compile or reload." );

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
			foreach ( var backup in backups )
				File.WriteAllBytes( backup.Key, backup.Value );
			foreach ( var file in newFiles )
			{
				if ( File.Exists( file ) )
					File.Delete( file );
			}

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
		HashSet<string>? newFiles = null )
	{
		Directory.CreateDirectory( root );
		foreach ( var file in files )
		{
			var absolute = Path.Combine( root, file.Key );
			if ( backups is not null && File.Exists( absolute ) )
				backups[absolute] = File.ReadAllBytes( absolute );
			else if ( newFiles is not null && !File.Exists( absolute ) )
				newFiles.Add( absolute );
			AtomicFile.WriteAllText( absolute, file.Value );
		}
	}

	private static void ResetStage( string stageRoot )
	{
		TryDeleteStage( stageRoot );
		Directory.CreateDirectory( stageRoot );
	}

	private static void TryDeleteStage( string stageRoot )
	{
		try
		{
			if ( Directory.Exists( stageRoot ) )
				Directory.Delete( stageRoot, true );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[Weapon Animator] could not clear generation stage '{stageRoot}': {ex.Message}" );
		}
	}

	public static string GetOutputFolder( WeaponAnimationDocument document ) =>
		ResolveOutputRoot( document );

	private static Dictionary<string, string> BuildFiles(
		WeaponAnimationDocument document,
		HostSkeleton skeleton,
		string relativeRoot )
	{
			var slug = WeaponAnimationDocument.Slugify( document.Output.AssetName );
			var files = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase);
			var referenceName = $"{slug}_host_reference.dmx";
			var hostName = $"{slug}_host.vmdl";
			var graphName = $"{slug}.vanmgrph";
			var prefabName = $"v_{slug}.prefab";
			files[referenceName] = DmxWriter.WriteReference( skeleton );

		var clipSources = new List<(WeaponAnimationClip Clip, string Source)>();
		foreach ( var clip in document.Clips.OrderBy( x => x.Name ) )
		{
			var clipName = $"{slug}_{WeaponAnimationNames.SequenceName( clip )}.smd";
			files[clipName] = SmdWriter.WriteClip( document, skeleton, clip );
			clipSources.Add( (clip, $"{relativeRoot}/{clipName}") );
		}

		var graphPath = document.Output.GenerateGraph && document.Graph.GenerateGraph
			? $"{relativeRoot}/{graphName}"
			: "";
		files[hostName] = ModelDocWriter.WriteHost(
			$"{relativeRoot}/{referenceName}",
			clipSources,
			graphPath,
			skeleton.Bones.Select( bone => bone.Name ) );

		if ( document.Output.GenerateGraph && document.Graph.GenerateGraph )
			files[graphName] = AnimGraphWriter.Write( document, $"{relativeRoot}/{hostName}" );

		var weaponModelPath = document.Source.CompiledModelPath;
		if ( NeedsGeneratedSourceAdapter( document ) )
		{
			var wrapperName = $"{slug}_source.vmdl";
			if ( Path.GetExtension( document.Source.SourcePath )
				.Equals( ".vmdl", StringComparison.OrdinalIgnoreCase ) )
			{
				var sourceAbsolute = global::Editor.FileSystem.Content.GetFullPath(
					document.Source.SourcePath );
				files[wrapperName] = ModelDocWriter.WriteVmdlSourceAdapter(
					File.ReadAllText( sourceAbsolute ),
					document.Source.SourceRootBoneName,
					ExcludedBranchRoots( document ) );
			}
			else
			{
				files[wrapperName] = ModelDocWriter.WriteSourceWrapper(
					document.Source.SourcePath,
					document.Source.SourceRootBoneName,
					ExcludedBranchRoots( document ) );
			}
			weaponModelPath = $"{relativeRoot}/{wrapperName}";
		}

		if ( document.Output.GeneratePrefab )
			files[prefabName] = PrefabWriter.Write(
				document,
				$"{relativeRoot}/{hostName}",
				weaponModelPath );

		return files;
	}

	private static bool NeedsGeneratedSourceAdapter(
		WeaponAnimationDocument document ) =>
		document.Source.NeedsModelDocWrapper
		|| Path.GetExtension( document.Source.SourcePath )
			.Equals( ".vmdl", StringComparison.OrdinalIgnoreCase );

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

	private static void CompileAndInspect(
		WeaponAnimationDocument document,
		string outputRoot,
		string relativeRoot,
		HostSkeleton skeleton,
		List<GenerationDiagnostic> diagnostics )
	{
		var slug = WeaponAnimationDocument.Slugify( document.Output.AssetName );
		var compileFiles = new List<string>();
		if ( NeedsGeneratedSourceAdapter( document ) )
			compileFiles.Add( $"{slug}_source.vmdl" );
		compileFiles.Add( $"{slug}_host.vmdl" );
		if ( document.Output.GenerateGraph && document.Graph.GenerateGraph )
			compileFiles.Add( $"{slug}.vanmgrph" );
		if ( document.Output.GeneratePrefab )
			compileFiles.Add( $"v_{slug}.prefab" );

		foreach ( var file in compileFiles )
		{
			var absolute = Path.Combine( outputRoot, file );
			var asset = AssetSystem.RegisterFile( absolute );
			if ( asset is null || !asset.Compile( true ) || !asset.IsCompiled )
			{
				diagnostics.Add( Diagnostic(
					ValidationSeverity.Error,
					"compile.failed",
					$"Failed to compile '{file}'.",
					absolute ) );
			}
			else
			{
				diagnostics.Add( Diagnostic(
					ValidationSeverity.Info,
					"compile.ok",
					$"Compiled '{file}'.",
					asset.Path ) );
			}
		}

		var hostPath = $"{relativeRoot}/{slug}_host.vmdl";
		var host = Model.Load( hostPath );
		if ( host is null || host.IsError || host.BoneCount != skeleton.Bones.Count )
		{
			diagnostics.Add( Diagnostic(
				ValidationSeverity.Error,
				"inspect.host",
				$"Host reload expected {skeleton.Bones.Count} bones but found {host?.BoneCount ?? 0}.",
				hostPath ) );
		}

		if ( NeedsGeneratedSourceAdapter( document ) )
		{
			var sourcePath = $"{relativeRoot}/{slug}_source.vmdl";
			var source = Model.Load( sourcePath );
			if ( source is null || source.IsError || source.BoneCount == 0 )
			{
				diagnostics.Add( Diagnostic(
					ValidationSeverity.Error,
					"inspect.source",
					"Generated source wrapper did not reload with a usable skeleton.",
					sourcePath ) );
			}
		}
	}

	private static string ResolveOutputRoot( WeaponAnimationDocument document )
	{
		var configured = string.IsNullOrWhiteSpace( document.Output.OutputFolder )
			? document.Output.GetDefaultRelativeFolder()
			: document.Output.OutputFolder.TrimStart( '/', '\\' );
		var full = global::Editor.FileSystem.Content.GetFullPath( configured );
		var assetsRoot = global::Editor.FileSystem.Content.GetFullPath( "/" ).NormalizeFilename( false );
		var normalized = full.NormalizeFilename( false );
		var relative = Path.GetRelativePath( assetsRoot, normalized );
		if ( Path.IsPathRooted( relative )
			|| relative.Equals( "..", StringComparison.Ordinal )
			|| relative.StartsWith( $"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal ) )
			throw new InvalidOperationException( "Generated output must stay inside the project's Assets folder." );
		return full;
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
