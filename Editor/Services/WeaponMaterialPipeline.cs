#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Editor;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

internal static class WeaponMaterialPipeline
{
	private const string PreviewMaterialFormatVersion = "preview-material-v2";

	private static readonly HashSet<string> SupportedImageExtensions =
		new( StringComparer.OrdinalIgnoreCase )
		{
			".png", ".tga", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".dds", ".exr"
		};

	private static readonly string[] NearbyFolderNames =
		["textures", "texture", "materials", "material", "maps"];

	private sealed record TextureCandidate(
		string Path,
		string GroupName,
		WeaponTextureChannel Channel,
		int Priority );

	internal sealed record GeneratedTextureCopy(
		string RelativePath,
		string SourceAbsolute );

	public static List<SourceMaterialBinding> DiscoverAndPreparePreview(
		string absoluteSource,
		string cacheRoot,
		Asset modelAsset,
		Model? model,
		List<RigAuditIssue> issues,
		IEnumerable<string>? knownMaterialSlots = null )
	{
		var candidates = DiscoverTextureCandidates( absoluteSource );
		var slots = DiscoverMaterialSlots( modelAsset, model );
		slots.AddRange( knownMaterialSlots?
			.Where( slot => !string.IsNullOrWhiteSpace( slot ) )
			?? [] );
		var embeddedSlots = DiscoverEmbeddedMaterialNames(
			absoluteSource,
			candidates.Select( candidate => candidate.GroupName ) )
			.Select( name => $"{name}.vmat" )
			.ToArray();
		slots.AddRange( embeddedSlots );
		var embeddedSet = embeddedSlots.ToHashSet( StringComparer.OrdinalIgnoreCase );
		slots = slots
			.Where( slot => !IsIgnoredMaterialPath( NormalizeMaterialPath( slot ) ) )
			.GroupBy( slot => NormalizeName( Path.GetFileNameWithoutExtension( slot ) ) )
			.Select( group => group.FirstOrDefault( embeddedSet.Contains ) ?? group.First() )
			.ToList();
		if ( slots.Count == 0 )
		{
			// Some interchange compilers omit unresolved material metadata. Texture set names
			// are the best deterministic fallback for the original slot labels.
			slots.AddRange( candidates
				.Select( candidate => candidate.GroupName )
				.Distinct( StringComparer.OrdinalIgnoreCase )
				.Select( name => $"{name}.vmat" ) );
		}

		var groups = candidates
			.GroupBy( candidate => NormalizeName( candidate.GroupName ) )
			.Where( group => !string.IsNullOrWhiteSpace( group.Key ) )
			.ToDictionary(
				group => group.Key,
				group => group.ToArray(),
				StringComparer.OrdinalIgnoreCase );
		var bindings = new List<SourceMaterialBinding>();
		var usedNames = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var slot in slots
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.OrderBy( name => name, StringComparer.OrdinalIgnoreCase ) )
		{
			var displayName = Path.GetFileNameWithoutExtension( slot );
			var outputName = UniqueOutputName(
				WeaponAnimationDocument.Slugify( displayName ),
				usedNames );
			var binding = new SourceMaterialBinding
			{
				SourceMaterialPath = StoredMaterialSlot( slot ),
				Name = displayName,
				OutputName = outputName
			};

			var matchingGroup = FindBestGroup( displayName, groups );
			if ( matchingGroup is not null )
			{
				foreach ( var channelGroup in matchingGroup
					.GroupBy( candidate => candidate.Channel )
					.OrderBy( group => group.Key ) )
				{
					var candidate = channelGroup
						.OrderByDescending( item => item.Priority )
						.ThenBy( item => item.Path, StringComparer.OrdinalIgnoreCase )
						.First();
					var hash = WeaponSourceImporter.HashFile( candidate.Path );
					var assetPath = EnsureTextureInsideAssets(
						candidate.Path,
						cacheRoot,
						hash );
					binding.Textures.Add( new SourceTextureMap
					{
						Channel = candidate.Channel,
						OriginalPath = candidate.Path,
						AssetPath = assetPath,
						Sha256 = hash
					} );
				}
			}

			if ( binding.FindTexture( WeaponTextureChannel.PackedOrm ) is not null
				&& !binding.HasUsableTextures )
			{
				issues.Add( new RigAuditIssue
				{
					Code = "material.packed_orm",
					Message =
						$"Material '{displayName}' only has a packed ORM texture. "
						+ "Separate color, normal, roughness, or metalness maps are required "
						+ "for automatic assignment.",
					Severity = ValidationSeverity.Warning
				} );
			}
			else if ( !binding.HasUsableTextures )
			{
				issues.Add( new RigAuditIssue
				{
					Code = "material.textures_missing",
					Message =
						$"No nearby texture set matched material '{displayName}'. "
						+ "That slot will use the default material.",
					Severity = ValidationSeverity.Warning
				} );
			}
			else if ( binding.FindTexture( WeaponTextureChannel.PackedOrm ) is not null
				&& binding.FindTexture( WeaponTextureChannel.Roughness ) is null
				&& binding.FindTexture( WeaponTextureChannel.Metalness ) is null )
			{
				issues.Add( new RigAuditIssue
				{
					Code = "material.packed_orm",
					Message =
						$"Material '{displayName}' only has a packed ORM texture. "
						+ "Separate roughness and metalness maps are required for automatic assignment.",
					Severity = ValidationSeverity.Warning
				} );
			}

			bindings.Add( binding );
		}

		PreparePreviewAssets( bindings, cacheRoot );
		return bindings;
	}

	public static IReadOnlyList<HostMaterialRemap> PreviewRemaps(
		IEnumerable<SourceMaterialBinding> bindings ) =>
		bindings
			.Where( binding => !string.IsNullOrWhiteSpace( binding.SourceMaterialPath ) )
			.Select( binding => new HostMaterialRemap(
				ResourceMaterialSlot( binding.SourceMaterialPath ),
				binding.HasUsableTextures
					&& !string.IsNullOrWhiteSpace( binding.PreviewMaterialPath )
						? binding.PreviewMaterialPath
						: "materials/default.vmat" ) )
			.ToArray();

	public static IReadOnlyList<HostMaterialRemap> OutputRemaps(
		WeaponAnimationDocument document,
		string relativeRoot )
	{
		var slug = WeaponAnimationDocument.Slugify( document.Output.AssetName );
		return document.Source.Materials
			.Where( binding => !string.IsNullOrWhiteSpace( binding.SourceMaterialPath ) )
			.Select( binding => new HostMaterialRemap(
				ResourceMaterialSlot( binding.SourceMaterialPath ),
				binding.HasUsableTextures
					? $"{relativeRoot}/materials/{slug}_{binding.OutputName}.vmat"
					: "materials/default.vmat" ) )
			.ToArray();
	}

	public static bool RequiresPreviewRefresh( WeaponAnimationDocument document ) =>
		document.Source.NeedsModelDocWrapper
		&& (document.Source.Materials.Count == 0
			|| document.Source.CompiledModelPath.StartsWith(
				".weaponanim-cache/",
				StringComparison.OrdinalIgnoreCase )
			|| document.Source.Materials.Any( binding =>
				Path.GetExtension( binding.SourceMaterialPath ).Equals(
					".vmat",
					StringComparison.OrdinalIgnoreCase ) )
			|| document.Source.Materials.Any( binding =>
				binding.HasUsableTextures
				&& !string.IsNullOrWhiteSpace( binding.PreviewMaterialPath )
				&& (binding.PreviewMaterialPath.StartsWith(
						".weaponanim-cache/",
						StringComparison.OrdinalIgnoreCase )
					|| binding.PreviewMaterialPath.Contains(
						"/texture-definitions/",
						StringComparison.OrdinalIgnoreCase )) ));

	public static Dictionary<string, string> BuildOutputTextFiles(
		WeaponAnimationDocument document,
		string relativeRoot )
	{
		var files = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		var slug = WeaponAnimationDocument.Slugify( document.Output.AssetName );
		foreach ( var binding in document.Source.Materials
			.Where( binding => binding.HasUsableTextures ) )
		{
			var texturePaths = new Dictionary<WeaponTextureChannel, string>();
			foreach ( var texture in binding.Textures
				.Where( texture => texture.Channel != WeaponTextureChannel.PackedOrm ) )
			{
				var imageName = OutputTextureImageName( slug, binding, texture );
				var imageRelative = $"textures/{imageName}";
				var texturePath = $"{relativeRoot}/{imageRelative}";
				texturePaths[texture.Channel] = texturePath;
			}

			files[$"materials/{slug}_{binding.OutputName}.vmat"] =
				WriteVmat( texturePaths );
		}

		return files;
	}

	public static IReadOnlyList<GeneratedTextureCopy> BuildOutputTextureCopies(
		WeaponAnimationDocument document )
	{
		var slug = WeaponAnimationDocument.Slugify( document.Output.AssetName );
		var copies = new List<GeneratedTextureCopy>();
		foreach ( var binding in document.Source.Materials
			.Where( binding => binding.HasUsableTextures ) )
		{
			foreach ( var texture in binding.Textures
				.Where( texture => texture.Channel != WeaponTextureChannel.PackedOrm ) )
			{
				var source = ResolveTextureAbsolute( texture );
				copies.Add( new GeneratedTextureCopy(
					$"textures/{OutputTextureImageName( slug, binding, texture )}",
					source ) );
			}
		}

		return copies;
	}

	internal static IReadOnlyList<SourceMaterialBinding> DiscoverForTests(
		IEnumerable<string> materialSlots,
		IEnumerable<string> texturePaths )
	{
		var candidates = texturePaths
			.Select( TryCreateCandidate )
			.Where( candidate => candidate is not null )
			.Cast<TextureCandidate>()
			.ToArray();
		var groups = candidates
			.GroupBy( candidate => NormalizeName( candidate.GroupName ) )
			.ToDictionary(
				group => group.Key,
				group => group.ToArray(),
				StringComparer.OrdinalIgnoreCase );
		var usedNames = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		return materialSlots
			.Where( slot => !IsIgnoredMaterialPath( NormalizeMaterialPath( slot ) ) )
			.Select( slot =>
		{
			var name = Path.GetFileNameWithoutExtension( slot );
			var binding = new SourceMaterialBinding
			{
				SourceMaterialPath = StoredMaterialSlot( slot ),
				Name = name,
				OutputName = UniqueOutputName(
					WeaponAnimationDocument.Slugify( name ),
					usedNames )
			};
			var group = FindBestGroup( name, groups );
			if ( group is not null )
			{
				binding.Textures = group
					.GroupBy( candidate => candidate.Channel )
					.Select( channel => channel.OrderByDescending( item => item.Priority ).First() )
					.Select( item => new SourceTextureMap
					{
						Channel = item.Channel,
						OriginalPath = item.Path,
						AssetPath = item.Path
					} )
					.ToList();
			}
			return binding;
		} ).ToArray();
	}

	internal static IReadOnlyList<string> MatchEmbeddedMaterialNamesForTests(
		IEnumerable<string> textureGroups,
		IEnumerable<string> embeddedStrings ) =>
		MatchEmbeddedMaterialNames( textureGroups, embeddedStrings );

	internal static string PreviewRevision(
		IEnumerable<SourceMaterialBinding> bindings )
	{
		var fingerprint = PreviewMaterialFormatVersion
			+ "\n"
			+ string.Join(
			"\n",
			bindings
				.OrderBy(
					binding => binding.SourceMaterialPath,
					StringComparer.OrdinalIgnoreCase )
				.Select( binding =>
					$"{NormalizeMaterialPath( binding.SourceMaterialPath )}|{binding.OutputName}|"
					+ string.Join(
						",",
						binding.Textures
							.OrderBy( texture => texture.Channel )
							.ThenBy(
								texture => texture.AssetPath,
								StringComparer.OrdinalIgnoreCase )
							.Select( texture =>
								$"{texture.Channel}:{texture.Sha256}:{texture.AssetPath}" ) ) ) );
		return WeaponSourceImporter.HashText( fingerprint )[..16];
	}

	internal static string PreviewRevisionRoot(
		string cacheRoot,
		IEnumerable<SourceMaterialBinding> bindings ) =>
		Path.Combine(
			LegalPreviewCacheRoot( cacheRoot ),
			PreviewRevision( bindings ) );

	internal static IReadOnlyList<string> PreviewMaterialAbsolutePaths(
		IEnumerable<SourceMaterialBinding> bindings ) =>
		bindings
			.Where( binding => binding.HasUsableTextures
				&& !string.IsNullOrWhiteSpace( binding.PreviewMaterialPath ) )
			.Select( binding => Path.Combine(
				WeaponSourceImporter.GetContentRoot(),
				binding.PreviewMaterialPath.Replace(
					'/',
					Path.DirectorySeparatorChar ) ) )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.ToArray();

	internal static IReadOnlyList<string> PreviewTextureAbsolutePaths(
		IEnumerable<SourceMaterialBinding> bindings ) =>
		bindings
			.SelectMany( binding => binding.Textures )
			.Where( texture => texture.Channel != WeaponTextureChannel.PackedOrm
				&& !string.IsNullOrWhiteSpace( texture.AssetPath ) )
			.Select( texture => Path.Combine(
				WeaponSourceImporter.GetContentRoot(),
				texture.AssetPath.Replace(
					'/',
					Path.DirectorySeparatorChar ) ) )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.ToArray();

	internal static string LegalPreviewRelativeRootForTests( string cacheRoot )
	{
		var documentFolder = Path.GetFileName(
			cacheRoot.TrimEnd(
				Path.DirectorySeparatorChar,
				Path.AltDirectorySeparatorChar ) );
		return $"weaponanim_preview_cache/{documentFolder}";
	}

	private static void PreparePreviewAssets(
		IEnumerable<SourceMaterialBinding> bindings,
		string cacheRoot )
	{
		var materialBindings = bindings.ToArray();
		var legalCacheRoot = PreviewRevisionRoot( cacheRoot, materialBindings );
		var materialRoot = Path.Combine( legalCacheRoot, "materials" );
		Directory.CreateDirectory( materialRoot );
		AtomicFile.WriteAllText(
			Path.Combine( legalCacheRoot, ".weaponanim-preview-version" ),
			PreviewMaterialFormatVersion );

		// Register source images before the directory watcher sees VMAT consumers. Otherwise
		// the dependency tracker can permanently mark a copied channel as "stopped existing".
		foreach ( var textureAbsolute in PreviewTextureAbsolutePaths( materialBindings ) )
			AssetSystem.RegisterFile( textureAbsolute );

		foreach ( var binding in materialBindings )
		{
			if ( !binding.HasUsableTextures )
			{
				binding.PreviewMaterialPath = "materials/default.vmat";
				continue;
			}

			var texturePaths = new Dictionary<WeaponTextureChannel, string>();
			foreach ( var texture in binding.Textures
				.Where( texture => texture.Channel != WeaponTextureChannel.PackedOrm ) )
			{
				texturePaths[texture.Channel] = texture.AssetPath;
			}

			var vmatAbsolute = Path.Combine( materialRoot, $"{binding.OutputName}.vmat" );
			AtomicFile.WriteAllText( vmatAbsolute, WriteVmat( texturePaths ) );
			binding.PreviewMaterialPath = WeaponSourceImporter.RelativeAssetPath( vmatAbsolute );
		}
	}

	private static List<string> DiscoverMaterialSlots( Asset modelAsset, Model? model )
	{
		var slots = new List<string>();
		try
		{
			slots.AddRange( modelAsset.GetUnrecognizedReferencePaths()
				.Where( IsSourceMaterialPath ) );
		}
		catch ( Exception ex )
		{
			Log.Warning(
				$"[Weapon Animator] could not inspect unresolved source material slots: {ex.Message}" );
		}

		if ( model is not null && !model.IsError )
		{
			try
			{
				slots.AddRange( model.Materials
					.Select( material => material.Name )
					.Where( IsSourceMaterialPath ) );
			}
			catch ( Exception ex )
			{
				Log.Warning(
					$"[Weapon Animator] could not inspect compiled model material slots: {ex.Message}" );
			}
		}
		return slots
			.Select( NormalizeMaterialPath )
			.Where( path => !path.Contains(
				".weaponanim-cache/",
				StringComparison.OrdinalIgnoreCase ) )
			.Where( path => !IsIgnoredMaterialPath( path ) )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.ToList();
	}

	private static bool IsSourceMaterialPath( string? path ) =>
		!string.IsNullOrWhiteSpace( path )
		&& Path.GetExtension( path ).Equals( ".vmat", StringComparison.OrdinalIgnoreCase );

	private static List<TextureCandidate> DiscoverTextureCandidates( string sourcePath )
	{
		var directories = NearbyDirectories( sourcePath );
		var files = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var directory in directories )
		{
			try
			{
				foreach ( var file in Directory.EnumerateFiles( directory )
					.Where( file => SupportedImageExtensions.Contains( Path.GetExtension( file ) ) )
					.Take( 512 ) )
				{
					files.Add( Path.GetFullPath( file ) );
				}
			}
			catch ( Exception ex )
			{
				Log.Warning(
					$"[Weapon Animator] could not inspect nearby texture folder '{directory}': {ex.Message}" );
			}
		}

		return files
			.Select( TryCreateCandidate )
			.Where( candidate => candidate is not null )
			.Cast<TextureCandidate>()
			.ToList();
	}

	private static IReadOnlyList<string> DiscoverEmbeddedMaterialNames(
		string sourcePath,
		IEnumerable<string> textureGroups )
	{
		if ( !Path.GetExtension( sourcePath ).Equals(
			".fbx",
			StringComparison.OrdinalIgnoreCase ) )
			return [];

		try
		{
			return MatchEmbeddedMaterialNames(
				textureGroups,
				ReadPrintableStrings( sourcePath ) );
		}
		catch ( Exception ex )
		{
			Log.Warning(
				$"[Weapon Animator] could not inspect embedded FBX material labels: {ex.Message}" );
			return [];
		}
	}

	private static IReadOnlyList<string> MatchEmbeddedMaterialNames(
		IEnumerable<string> textureGroups,
		IEnumerable<string> embeddedStrings )
	{
		var strings = embeddedStrings
			.Where( value => value.Length is >= 2 and <= 128
				&& !value.Contains( '/' )
				&& !value.Contains( '\\' )
				&& !value.Contains( '.' ) )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.ToArray();
		var names = new List<string>();
		foreach ( var group in textureGroups
			.Distinct( StringComparer.OrdinalIgnoreCase ) )
		{
			var normalizedGroup = NormalizeName( group );
			var match = strings
				.Where( value => NormalizeName( value ).Equals(
					normalizedGroup,
					StringComparison.OrdinalIgnoreCase ) )
				.OrderBy( value => value.Length )
				.ThenBy( value => value, StringComparer.OrdinalIgnoreCase )
				.FirstOrDefault();
			if ( !string.IsNullOrWhiteSpace( match ) )
				names.Add( match );
		}
		return names.Distinct( StringComparer.OrdinalIgnoreCase ).ToArray();
	}

	private static IEnumerable<string> ReadPrintableStrings( string path )
	{
		using var stream = File.OpenRead( path );
		var builder = new StringBuilder();
		var buffer = new byte[64 * 1024];
		int count;
		while ( (count = stream.Read( buffer, 0, buffer.Length )) > 0 )
		{
			for ( var index = 0; index < count; index++ )
			{
				var value = buffer[index];
				if ( value is >= 32 and <= 126 )
				{
					if ( builder.Length < 512 )
						builder.Append( (char)value );
					continue;
				}

				if ( builder.Length >= 2 )
					yield return builder.ToString();
				builder.Clear();
			}
		}
		if ( builder.Length >= 2 )
			yield return builder.ToString();
	}

	private static IEnumerable<string> NearbyDirectories( string sourcePath )
	{
		var sourceDirectory = Path.GetDirectoryName( sourcePath );
		if ( string.IsNullOrWhiteSpace( sourceDirectory ) )
			yield break;

		var found = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		if ( found.Add( sourceDirectory ) )
			yield return sourceDirectory;

		foreach ( var root in new[]
			{
				sourceDirectory,
				Directory.GetParent( sourceDirectory )?.FullName
			}.Where( root => !string.IsNullOrWhiteSpace( root ) ) )
		{
			foreach ( var folder in NearbyFolderNames )
			{
				var candidate = Path.Combine( root!, folder );
				if ( Directory.Exists( candidate ) && found.Add( candidate ) )
					yield return candidate;
			}

			IEnumerable<string> children;
			try
			{
				children = Directory.EnumerateDirectories( root! ).ToArray();
			}
			catch
			{
				continue;
			}

			foreach ( var child in children.Where( child =>
				NearbyFolderNames.Any( folder =>
					Path.GetFileName( child ).Contains(
						folder,
						StringComparison.OrdinalIgnoreCase ) ) ) )
			{
				if ( found.Add( child ) )
					yield return child;
			}
		}
	}

	private static TextureCandidate? TryCreateCandidate( string path )
	{
		var stem = Path.GetFileNameWithoutExtension( path );
		var normalized = NormalizeSeparators( stem );
		var patterns = new (string Token, WeaponTextureChannel Channel, int Priority)[]
		{
			("occlusion_roughness_metallic", WeaponTextureChannel.PackedOrm, 100),
			("occlusionroughnessmetallic", WeaponTextureChannel.PackedOrm, 100),
			("normal_opengl", WeaponTextureChannel.Normal, 145),
			("normal_gl", WeaponTextureChannel.Normal, 145),
			("nrm_gl", WeaponTextureChannel.Normal, 140),
			("normal_directx", WeaponTextureChannel.Normal, 80),
			("normal_dx", WeaponTextureChannel.Normal, 80),
			("nrm_dx", WeaponTextureChannel.Normal, 75),
			("base_color", WeaponTextureChannel.BaseColor, 120),
			("basecolor", WeaponTextureChannel.BaseColor, 120),
			("albedo", WeaponTextureChannel.BaseColor, 115),
			("diffuse", WeaponTextureChannel.BaseColor, 110),
			("color", WeaponTextureChannel.BaseColor, 100),
			("ambient_occlusion", WeaponTextureChannel.AmbientOcclusion, 120),
			("ambientocclusion", WeaponTextureChannel.AmbientOcclusion, 120),
			("occlusion", WeaponTextureChannel.AmbientOcclusion, 100),
			("roughness", WeaponTextureChannel.Roughness, 120),
			("rough", WeaponTextureChannel.Roughness, 110),
			("metalness", WeaponTextureChannel.Metalness, 120),
			("metallic", WeaponTextureChannel.Metalness, 120),
			("metal", WeaponTextureChannel.Metalness, 100),
			("normal", WeaponTextureChannel.Normal, 110),
			("nrm", WeaponTextureChannel.Normal, 105),
			("diff", WeaponTextureChannel.BaseColor, 90),
			("ao", WeaponTextureChannel.AmbientOcclusion, 90),
			("orm", WeaponTextureChannel.PackedOrm, 90),
			("rma", WeaponTextureChannel.PackedOrm, 85),
			("mra", WeaponTextureChannel.PackedOrm, 85)
		};

		foreach ( var pattern in patterns )
		{
			var marker = $"_{pattern.Token}";
			var index = normalized.LastIndexOf( marker, StringComparison.Ordinal );
			if ( index < 0 && normalized.Equals( pattern.Token, StringComparison.Ordinal ) )
				index = 0;
			if ( index < 0 )
				continue;

			var group = normalized[..index].Trim( '_' );
			if ( string.IsNullOrWhiteSpace( group ) )
				continue;
			return new TextureCandidate(
				path,
				group,
				pattern.Channel,
				pattern.Priority );
		}

		return null;
	}

	private static TextureCandidate[]? FindBestGroup(
		string materialName,
		IReadOnlyDictionary<string, TextureCandidate[]> groups )
	{
		var normalizedMaterial = NormalizeName( materialName );
		var best = groups
			.Select( pair => new
			{
				pair.Value,
				Score = MatchScore( normalizedMaterial, pair.Key )
			} )
			.OrderByDescending( item => item.Score )
			.FirstOrDefault();
		return best is not null && best.Score > 0 ? best.Value : null;
	}

	private static int MatchScore( string material, string group )
	{
		if ( material.Equals( group, StringComparison.OrdinalIgnoreCase ) )
			return 10000;
		if ( material.Contains( group, StringComparison.OrdinalIgnoreCase )
			|| group.Contains( material, StringComparison.OrdinalIgnoreCase ) )
			return 1000 + Math.Min( material.Length, group.Length );
		return 0;
	}

	private static string EnsureTextureInsideAssets(
		string source,
		string cacheRoot,
		string hash )
	{
		// Preview revisions reference immutable, legal resource names rather than arbitrary
		// user filenames or an image which can change underneath the active model.
		var sourceRoot = Path.Combine(
			LegalPreviewCacheRoot( cacheRoot ),
			"source-textures" );
		Directory.CreateDirectory( sourceRoot );
		var fileName =
			$"{WeaponAnimationDocument.Slugify( Path.GetFileNameWithoutExtension( source ) )}"
			+ $"_{hash[..12]}{Path.GetExtension( source ).ToLowerInvariant()}";
		var destination = Path.Combine( sourceRoot, fileName );
		if ( !File.Exists( destination )
			|| !WeaponSourceImporter.HashFile( destination ).Equals(
				hash,
				StringComparison.OrdinalIgnoreCase ) )
		{
			File.Copy( source, destination, true );
		}
		return WeaponSourceImporter.RelativeAssetPath( destination );
	}

	private static string ResolveTextureAbsolute( SourceTextureMap texture )
	{
		if ( !string.IsNullOrWhiteSpace( texture.AssetPath ) )
		{
			var candidate = Path.Combine(
				WeaponSourceImporter.GetContentRoot(),
				texture.AssetPath.Replace( '/', Path.DirectorySeparatorChar ) );
			if ( File.Exists( candidate ) )
				return candidate;
		}

		if ( !string.IsNullOrWhiteSpace( texture.OriginalPath )
			&& File.Exists( texture.OriginalPath ) )
			return Path.GetFullPath( texture.OriginalPath );

		throw new FileNotFoundException(
			$"Texture source for {texture.Channel} is missing.",
			texture.AssetPath );
	}

	private static string OutputTextureImageName(
		string slug,
		SourceMaterialBinding binding,
		SourceTextureMap texture )
	{
		var extension = Path.GetExtension(
			string.IsNullOrWhiteSpace( texture.AssetPath )
				? texture.OriginalPath
				: texture.AssetPath );
		if ( !SupportedImageExtensions.Contains( extension ) )
			extension = ".png";
		return $"{slug}_{binding.OutputName}_{ChannelSuffix( texture.Channel )}"
			+ extension.ToLowerInvariant();
	}

	private static string WriteVmat(
		IReadOnlyDictionary<WeaponTextureChannel, string> textures )
	{
		string TexturePath( WeaponTextureChannel channel, string fallback ) =>
			textures.TryGetValue( channel, out var path )
				? path.Replace( '\\', '/' )
				: fallback;
		var metalness = textures.TryGetValue(
			WeaponTextureChannel.Metalness,
			out var metalnessPath )
				? $$"""

						F_METALNESS_TEXTURE 1
						TextureMetalness "{{metalnessPath.Replace( '\\', '/' )}}"
					"""
				: "";

		return $$"""
			// SboxWeaponAnimator generated material.
			Layer0
			{
				shader "shaders/complex.shader"

				F_SPECULAR 1
				TextureAmbientOcclusion "{{TexturePath( WeaponTextureChannel.AmbientOcclusion, "materials/default/default_ao.tga" )}}"
				TextureColor "{{TexturePath( WeaponTextureChannel.BaseColor, "materials/default/default_color.tga" )}}"
				TextureNormal "{{TexturePath( WeaponTextureChannel.Normal, "materials/default/default_normal.tga" )}}"
				TextureRoughness "{{TexturePath( WeaponTextureChannel.Roughness, "materials/default/default_rough.tga" )}}"{{metalness}}
				g_flModelTintAmount "1.000"
				g_vColorTint "[1.000000 1.000000 1.000000 0.000000]"
				g_flRoughnessScaleFactor "1.000"
				g_bFogEnabled "1"
			}
			""";
	}

	private static string NormalizeMaterialPath( string value )
	{
		var normalized = value.Replace( '\\', '/' ).Trim();
		if ( !Path.HasExtension( normalized ) )
			normalized += ".vmat";
		return normalized;
	}

	internal static string StoredMaterialSlot( string value )
	{
		var normalized = NormalizeMaterialPath( value );
		return normalized.EndsWith( ".vmat", StringComparison.OrdinalIgnoreCase )
			? normalized[..^5]
			: normalized;
	}

	private static string ResourceMaterialSlot( string value ) =>
		NormalizeMaterialPath( value );

	private static bool IsIgnoredMaterialPath( string path ) =>
		path.Equals( "materials/default.vmat", StringComparison.OrdinalIgnoreCase )
		|| path.Equals( "materials/error.vmat", StringComparison.OrdinalIgnoreCase )
		|| path.Equals(
			"materials/tools/toolsinvisible.vmat",
			StringComparison.OrdinalIgnoreCase );

	private static string LegalPreviewCacheRoot( string cacheRoot )
	{
		return Path.Combine(
			WeaponSourceImporter.GetContentRoot(),
			LegalPreviewRelativeRootForTests( cacheRoot ).Replace(
				'/',
				Path.DirectorySeparatorChar ) );
	}

	private static string NormalizeSeparators( string value )
	{
		var builder = new StringBuilder( value.Length );
		var previousSeparator = false;
		foreach ( var character in value.ToLowerInvariant() )
		{
			var separator = !char.IsLetterOrDigit( character );
			if ( separator )
			{
				if ( !previousSeparator )
					builder.Append( '_' );
			}
			else
			{
				builder.Append( character );
			}
			previousSeparator = separator;
		}
		return builder.ToString().Trim( '_' );
	}

	private static string NormalizeName( string value ) =>
		new( value
			.Where( char.IsLetterOrDigit )
			.Select( char.ToLowerInvariant )
			.ToArray() );

	private static string UniqueOutputName(
		string baseName,
		HashSet<string> usedNames )
	{
		if ( string.IsNullOrWhiteSpace( baseName ) )
			baseName = "material";
		var candidate = baseName;
		var suffix = 2;
		while ( !usedNames.Add( candidate ) )
			candidate = $"{baseName}_{suffix++}";
		return candidate;
	}

	private static string ChannelSuffix( WeaponTextureChannel channel ) => channel switch
	{
		WeaponTextureChannel.BaseColor => "color",
		WeaponTextureChannel.Normal => "normal",
		WeaponTextureChannel.Roughness => "roughness",
		WeaponTextureChannel.Metalness => "metalness",
		WeaponTextureChannel.AmbientOcclusion => "ao",
		_ => "orm"
	};
}
