#nullable enable annotations

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public sealed record HostWeaponMesh(
	string SourcePath,
	string SourceRootBoneName,
	Transform ImportTransform,
	IReadOnlyList<string> ExcludedBranchRoots );

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
		IEnumerable<HostAttachment>? attachments = null )
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
		var materialGroup = BuildMaterialGroup( weaponMesh is not null );
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
					base_model_name = ""
				}
			}
			""";
	}

	private static string BuildMaterialGroup( bool includesImportedWeapon )
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

		// Raw interchange models commonly contain material labels rather than valid VMAT paths.
		// Keep the host carrier invisible while safely substituting unresolved weapon materials.
		return """
								{
									_class = "DefaultMaterialGroup"
									remaps =
									[
										{
											from = "materials/tools/toolsinvisible.vmat"
											to = "materials/tools/toolsinvisible.vmat"
										},
									]
									use_global_default = true
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
		System.Collections.Generic.IEnumerable<string>? excludedBranchRoots = null )
	{
		var modifierList = BuildSourceModifierList(
			sourceRootBoneName,
			excludedBranchRoots );

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
							{
								_class = "DefaultMaterialGroup"
								remaps = [ ]
								use_global_default = true
								global_default_material = "materials/default.vmat"
							},
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
		System.Collections.Generic.IEnumerable<string>? excludedBranchRoots = null )
	{
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
