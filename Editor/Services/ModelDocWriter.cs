#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public sealed record HostWeaponMesh(
	string SourcePath,
	string SourceRootBoneName,
	Transform ImportTransform,
	IReadOnlyList<string> ExcludedBranchRoots,
	IReadOnlyList<HostMaterialRemap>? MaterialRemaps = null );

public sealed record HostMaterialRemap(
	string SourceMaterial,
	string TargetMaterial );

public sealed record HostAttachment(
	string Name,
	string ParentBone,
	Vector3 LocalPosition,
	Rotation LocalRotation );

public static class ModelDocWriter
{
	public const string Header =
		"<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} " +
		"format:modeldoc29:version{3cec427c-1b0e-4d48-a90a-0436f33a6041} -->";

	public static string WriteHost(
		string referenceMesh,
		IEnumerable<(WeaponAnimationClip Clip, string Source)> clips,
		string animGraphPath,
		IEnumerable<string> preservedBones,
		HostWeaponMesh? weaponMesh = null,
		IEnumerable<HostAttachment>? attachments = null,
		string baseModelPath = "",
		IEnumerable<HostMaterialRemap>? baseMaterialRemaps = null )
	{
		var animationNodes = new StringBuilder();
		foreach ( var item in clips.OrderBy( x => x.Clip.Name ) )
		{
			animationNodes.AppendLine( $$"""
								{
									_class = "AnimFile"
									name = "{{WeaponAnimationNames.SequenceName( item.Clip )}}"
									activity_name = ""
									activity_weight = 1
									weight_list_name = ""
									fade_in_time = 0.1
									fade_out_time = 0.1
									looping = {{item.Clip.Loop.ToString().ToLowerInvariant()}}
									delta = false
									worldSpace = false
									hidden = false
									anim_markup_ordered = false
									disable_compression = false
									disable_interpolation = false
									enable_scale = true
									source_filename = "{{item.Source}}"
									start_frame = -1
									end_frame = -1
									framerate = {{F( item.Clip.SampleRate )}}
									take = 0
									reverse = false
								},
			""" );
		}

		var weaponMeshNode = BuildWeaponMeshNode( weaponMesh );
		var materialGroup = BuildMaterialGroup(
			weaponMesh is not null || !string.IsNullOrWhiteSpace( baseModelPath ),
			weaponMesh?.MaterialRemaps ?? baseMaterialRemaps );
		var attachmentList = BuildAttachmentList( attachments );
		var boneMarkupNodes = new StringBuilder();
		foreach ( var boneName in preservedBones
			.Where( name => !string.IsNullOrWhiteSpace( name ) )
			.Distinct( System.StringComparer.OrdinalIgnoreCase )
			.OrderBy( name => name, System.StringComparer.OrdinalIgnoreCase ) )
		{
			boneMarkupNodes.AppendLine( $$"""
										{
											_class = "BoneMarkup"
											target_bone = "{{Escape( boneName )}}"
											ignore_Translation = false
											ignore_rotation = false
											do_not_discard = true
										},
				""" );
		}

		return $$"""
			{{Header}}
			// SboxWeaponAnimator generated file. Ownership is recorded in weaponanim.manifest.json.
			{
				rootNode =
				{
					_class = "RootNode"
					children =
					[
						{
							_class = "MaterialGroupList"
							children =
							[
			{{materialGroup}}
							]
						},
						{
							_class = "RenderMeshList"
							children =
							[
								{
									_class = "RenderMeshFile"
									name = "animation_host"
									filename = "{{referenceMesh}}"
									import_translation = [ 0.0, 0.0, 0.0 ]
									import_rotation = [ 0.0, 0.0, 0.0 ]
									import_scale = 1.0
									align_origin_x_type = "None"
									align_origin_y_type = "None"
									align_origin_z_type = "None"
									parent_bone = ""
									import_filter = { exclude_by_default = false exception_list = [ ] }
								},
			{{weaponMeshNode}}
							]
						},
						{
							_class = "AnimationList"
							children =
							[
			{{animationNodes}}				]
							default_root_bone_name = ""
						},
			{{attachmentList}}
						{
							_class = "BoneMarkupList"
							children =
							[
			{{boneMarkupNodes}}				]
							bone_cull_type = "None"
						},
					]
					model_archetype = ""
					primary_associated_entity = ""
					anim_graph_name = "{{animGraphPath}}"
					base_model_name = "{{Escape( baseModelPath )}}"
				}
			}
			""";
	}

	private static string BuildMaterialGroup(
		bool includesImportedWeapon,
		IEnumerable<HostMaterialRemap>? materialRemaps )
	{
		if ( !includesImportedWeapon )
		{
			return """
								{
									_class = "DefaultMaterialGroup"
									remaps = [ ]
									use_global_default = false
									global_default_material = "materials/default.vmat"
								},
				""";
		}

		var remaps = new List<HostMaterialRemap>
		{
			new(
				"materials/tools/toolsinvisible.vmat",
				"materials/tools/toolsinvisible.vmat" )
		};
		remaps.AddRange( materialRemaps?
			.Where( remap => !string.IsNullOrWhiteSpace( remap.SourceMaterial )
				&& !string.IsNullOrWhiteSpace( remap.TargetMaterial ) )
			?? [] );

		var remapText = new StringBuilder();
		foreach ( var remap in remaps
			.DistinctBy(
				remap => remap.SourceMaterial,
				System.StringComparer.OrdinalIgnoreCase )
			.OrderBy(
				remap => remap.SourceMaterial,
				System.StringComparer.OrdinalIgnoreCase ) )
		{
			remapText.AppendLine( $$"""
										{
											from = "{{Escape( remap.SourceMaterial )}}"
											to = "{{Escape( remap.TargetMaterial )}}"
										},
				""" );
		}

		// Every imported slot is mapped independently. Global substitution would collapse
		// multi-material weapons to a single texture.
		return $$"""
							{
								_class = "DefaultMaterialGroup"
								remaps =
								[
			{{remapText}}					]
								use_global_default = false
								global_default_material = "materials/default.vmat"
							},
			""";
	}

	private static string BuildAttachmentList( IEnumerable<HostAttachment>? attachments )
	{
		var items = attachments?
			.Where( x => !string.IsNullOrWhiteSpace( x.Name )
				&& !string.IsNullOrWhiteSpace( x.ParentBone ) )
			.OrderBy( x => x.Name, System.StringComparer.OrdinalIgnoreCase )
			.ToArray() ?? [];
		if ( items.Length == 0 )
			return "";

		var nodes = new StringBuilder();
		foreach ( var attachment in items )
		{
			var angles = attachment.LocalRotation.Angles();
			nodes.AppendLine( $$"""
								{
									_class = "Attachment"
									name = "{{Escape( attachment.Name )}}"
									parent_bone = "{{Escape( attachment.ParentBone )}}"
									relative_origin = [ {{F( attachment.LocalPosition.x )}}, {{F( attachment.LocalPosition.y )}}, {{F( attachment.LocalPosition.z )}} ]
									relative_angles = [ {{F( angles.pitch )}}, {{F( angles.yaw )}}, {{F( angles.roll )}} ]
									weight = 1.0
									ignore_rotation = false
								},
				""" );
		}

		return $$"""
						{
							_class = "AttachmentList"
							children =
							[
			{{nodes}}				]
						},

			""";
	}

	private static string BuildWeaponMeshNode( HostWeaponMesh? source )
	{
		if ( source is null || string.IsNullOrWhiteSpace( source.SourcePath ) )
			return "";

		var modifiers = new StringBuilder();
		if ( !string.IsNullOrWhiteSpace( source.SourceRootBoneName )
			&& !source.SourceRootBoneName.Equals(
				"weapon_root",
				System.StringComparison.OrdinalIgnoreCase ) )
		{
			modifiers.AppendLine( $$"""
											{
												_class = "RenameBonePrefix"
												prefix_to_match = "{{Escape( source.SourceRootBoneName )}}"
												replacement = "weapon_root"
												allow_nonmatching_bones = true
											},
				""" );
		}

		var excluded = source.ExcludedBranchRoots
			.Where( x => !string.IsNullOrWhiteSpace( x ) )
			.Distinct( System.StringComparer.OrdinalIgnoreCase )
			.OrderBy( x => x, System.StringComparer.OrdinalIgnoreCase )
			.ToArray();
		if ( excluded.Length > 0 )
		{
			var names = string.Join(
				"\n",
				excluded.Select( x => $"\t\t\t\t\t\t\t\t\t\t\t\"{Escape( x )}\"," ) );
			modifiers.AppendLine( $$"""
											{
												_class = "RemoveBoneAndChildren"
												bone_names =
												[
				{{names}}
												]
											},
				""" );
		}

		var children = modifiers.Length == 0
			? ""
			: $$"""
										children =
										[
				{{modifiers}}							]

				""";
		var angles = source.ImportTransform.Rotation.Angles();
		return $$"""
								{
									_class = "RenderMeshFile"
									name = "weapon"
									filename = "{{Escape( source.SourcePath )}}"
									import_translation = [ {{F( source.ImportTransform.Position.x )}}, {{F( source.ImportTransform.Position.y )}}, {{F( source.ImportTransform.Position.z )}} ]
									import_rotation = [ {{F( angles.pitch )}}, {{F( angles.yaw )}}, {{F( angles.roll )}} ]
									import_scale = {{F( source.ImportTransform.Scale.x )}}
									align_origin_x_type = "None"
									align_origin_y_type = "None"
									align_origin_z_type = "None"
									parent_bone = ""
									import_filter = { exclude_by_default = false exception_list = [ ] }
			{{children}}					},
			""";
	}

	public static string WriteSourceWrapper(
		string sourcePath,
		string sourceRootBoneName = "",
		System.Collections.Generic.IEnumerable<string>? excludedBranchRoots = null,
		System.Collections.Generic.IEnumerable<HostMaterialRemap>? materialRemaps = null )
	{
		var modifierList = BuildSourceModifierList(
			sourceRootBoneName,
			excludedBranchRoots );
		var materialGroup = BuildMaterialGroup( true, materialRemaps );

		return $$"""
			{{Header}}
			// SboxWeaponAnimator generated source wrapper.
			{
			rootNode =
			{
				_class = "RootNode"
				children =
				[
					{
						_class = "MaterialGroupList"
						children =
						[
			{{materialGroup}}
						]
					},
					{
						_class = "RenderMeshList"
						children =
						[
							{
								_class = "RenderMeshFile"
								name = "source_weapon"
								filename = "{{sourcePath}}"
								import_translation = [ 0.0, 0.0, 0.0 ]
								import_rotation = [ 0.0, 0.0, 0.0 ]
								import_scale = 1.0
								align_origin_x_type = "None"
								align_origin_y_type = "None"
								align_origin_z_type = "None"
								parent_bone = ""
								import_filter = { exclude_by_default = false exception_list = [ ] }
							},
						]
						},
						{ _class = "BoneMarkupList" bone_cull_type = "None" },
			{{modifierList}}			
					]
					model_archetype = ""
				primary_associated_entity = ""
				anim_graph_name = ""
				base_model_name = ""
				}
			}
			""";
	}

	public static string WriteVmdlSourceAdapter(
		string sourceModelDoc,
		string sourceRootBoneName,
		System.Collections.Generic.IEnumerable<string>? excludedBranchRoots = null,
		Transform? importTransform = null )
	{
		if ( importTransform is { } placement )
			sourceModelDoc = ApplyRenderMeshImportTransform( sourceModelDoc, placement );

		var modifierList = BuildSourceModifierList(
			sourceRootBoneName,
			excludedBranchRoots );
		if ( string.IsNullOrWhiteSpace( modifierList ) )
			return sourceModelDoc;

		var rootIndex = sourceModelDoc.IndexOf( "rootNode", System.StringComparison.Ordinal );
		var childrenIndex = rootIndex < 0
			? -1
			: sourceModelDoc.IndexOf( "children", rootIndex, System.StringComparison.Ordinal );
		var openingBracket = childrenIndex < 0
			? -1
			: sourceModelDoc.IndexOf( '[', childrenIndex );
		if ( openingBracket < 0 )
			throw new System.InvalidOperationException( "The source VMDL does not expose a writable root child list." );

		var insertion = "\n" + modifierList.Trim() + "\n";
		return sourceModelDoc.Insert( openingBracket + 1, insertion );
	}

	internal static string ApplyRenderMeshImportTransform(
		string sourceModelDoc,
		Transform placement )
	{
		var blocks = new List<(int Start, int End)>();
		var search = 0;
		while ( true )
		{
			var classIndex = sourceModelDoc.IndexOf(
				"_class = \"RenderMeshFile\"",
				search,
				StringComparison.Ordinal );
			if ( classIndex < 0 )
				break;
			var opening = sourceModelDoc.LastIndexOf( '{', classIndex );
			var closing = opening < 0 ? -1 : FindClosingBrace( sourceModelDoc, opening );
			if ( opening < 0 || closing < 0 )
				throw new InvalidOperationException(
					"The source VMDL contains a malformed RenderMeshFile node." );
			blocks.Add( (opening, closing + 1) );
			search = closing + 1;
		}

		if ( blocks.Count == 0 )
			throw new InvalidOperationException(
				"The source VMDL does not contain an editable RenderMeshFile node." );

		var result = new StringBuilder( sourceModelDoc );
		foreach ( var (start, end) in blocks.OrderByDescending( block => block.Start ) )
		{
			var block = sourceModelDoc[start..end];
			var sourceAngles = ReadVector( block, "import_rotation", Vector3.Zero );
			var source = new Transform(
				ReadVector( block, "import_translation", Vector3.Zero ),
				Rotation.From( sourceAngles.x, sourceAngles.y, sourceAngles.z ),
				new Vector3( ReadScalar( block, "import_scale", 1 ) ) );
			var combined = new Transform(
				placement.PointToWorld( source.Position ),
				placement.Rotation * source.Rotation,
				placement.Scale * source.Scale );
			var angles = combined.Rotation.Angles();
			block = ReplaceField(
				block,
				"import_translation",
				$"[ {F( combined.Position.x )}, {F( combined.Position.y )}, {F( combined.Position.z )} ]" );
			block = ReplaceField(
				block,
				"import_rotation",
				$"[ {F( angles.pitch )}, {F( angles.yaw )}, {F( angles.roll )} ]" );
			block = ReplaceField( block, "import_scale", F( combined.Scale.x ) );
			result.Remove( start, end - start );
			result.Insert( start, block );
		}
		return result.ToString();
	}

	private static string ReplaceField( string block, string name, string value )
	{
		var pattern = $@"(?m)^(\s*){Regex.Escape( name )}\s*=\s*(\[[^\]]*\]|[^\r\n]+)";
		if ( Regex.IsMatch( block, pattern ) )
			return new Regex( pattern ).Replace(
				block,
				$"${{1}}{name} = {value}",
				1 );

		var classLine = block.IndexOf(
			"_class = \"RenderMeshFile\"",
			StringComparison.Ordinal );
		var lineEnd = classLine < 0 ? -1 : block.IndexOf( '\n', classLine );
		if ( lineEnd < 0 )
			throw new InvalidOperationException(
				$"The source RenderMeshFile cannot receive '{name}'." );
		var indentation = Regex.Match( block[(block.LastIndexOf( '\n', classLine ) + 1)..], @"^\s*" ).Value;
		return block.Insert( lineEnd + 1, $"{indentation}{name} = {value}\n" );
	}

	private static Vector3 ReadVector( string block, string name, Vector3 fallback )
	{
		var match = Regex.Match(
			block,
			$@"(?m)^\s*{Regex.Escape( name )}\s*=\s*\[\s*({NumberPattern})\s*,\s*({NumberPattern})\s*,\s*({NumberPattern})\s*\]" );
		return match.Success
			? new Vector3(
				ParseNumber( match.Groups[1].Value ),
				ParseNumber( match.Groups[2].Value ),
				ParseNumber( match.Groups[3].Value ) )
			: fallback;
	}

	private static float ReadScalar( string block, string name, float fallback )
	{
		var match = Regex.Match(
			block,
			$@"(?m)^\s*{Regex.Escape( name )}\s*=\s*({NumberPattern})" );
		return match.Success ? ParseNumber( match.Groups[1].Value ) : fallback;
	}

	private static float ParseNumber( string value ) =>
		float.Parse( value, NumberStyles.Float, CultureInfo.InvariantCulture );

	private static int FindClosingBrace( string text, int opening )
	{
		var depth = 0;
		var quoted = false;
		var escaped = false;
		for ( var index = opening; index < text.Length; index++ )
		{
			var character = text[index];
			if ( quoted )
			{
				if ( escaped )
					escaped = false;
				else if ( character == '\\' )
					escaped = true;
				else if ( character == '"' )
					quoted = false;
				continue;
			}
			if ( character == '"' )
			{
				quoted = true;
				continue;
			}
			if ( character == '/' && index + 1 < text.Length )
			{
				if ( text[index + 1] == '/' )
				{
					index = text.IndexOf( '\n', index + 2 );
					if ( index < 0 )
						return -1;
					continue;
				}
				if ( text[index + 1] == '*' )
				{
					index = text.IndexOf( "*/", index + 2, StringComparison.Ordinal );
					if ( index < 0 )
						return -1;
					index++;
					continue;
				}
			}
			if ( character == '{' )
				depth++;
			else if ( character == '}' && --depth == 0 )
				return index;
		}
		return -1;
	}

	private const string NumberPattern = @"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?";

	private static string BuildSourceModifierList(
		string sourceRootBoneName,
		System.Collections.Generic.IEnumerable<string>? excludedBranchRoots )
	{
		var modifiers = new System.Collections.Generic.List<string>();
		if ( !string.IsNullOrWhiteSpace( sourceRootBoneName )
			&& !sourceRootBoneName.Equals( "weapon_root", System.StringComparison.OrdinalIgnoreCase ) )
		{
			modifiers.Add( $$"""
									{
										_class = "RenameBone"
										original_bone_name = "{{Escape( sourceRootBoneName )}}"
										new_bone_name = "weapon_root"
									},
				""" );
		}

		var excluded = excludedBranchRoots?
			.Where( x => !string.IsNullOrWhiteSpace( x ) )
			.Distinct( System.StringComparer.OrdinalIgnoreCase )
			.OrderBy( x => x, System.StringComparer.OrdinalIgnoreCase )
			.ToArray() ?? [];
		if ( excluded.Length > 0 )
		{
			var boneNames = string.Join(
				"\n",
				excluded.Select( x => $"\t\t\t\t\t\t\t\t\t\"{Escape( x )}\"," ) );
			modifiers.Add( $$"""
									{
										_class = "RemoveBoneAndChildren"
										bone_names =
										[
										{{boneNames}}
										]
									},
				""" );
		}

		var modifierList = modifiers.Count == 0
				? ""
				: $$"""
							{
								_class = "ModelModifierList"
								children =
								[
								{{string.Join( "\n", modifiers )}}
								]
							},

						""";
		return modifierList;
	}

	private static string F( float value ) =>
		value.ToString( "0.######", CultureInfo.InvariantCulture );

	private static string Escape( string value ) =>
		value.Replace( "\\", "\\\\" ).Replace( "\"", "\\\"" );
}
