#nullable enable annotations

using System;
using System.IO;
using System.Linq;
using Editor;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public sealed class PreviewHostResult
{
	public bool Success { get; init; }
	public string Message { get; init; } = "";
	public string ModelPath { get; init; } = "";
}

public static class PreviewHostBuilder
{
	public static PreviewHostResult Build( WeaponAnimationDocument document )
	{
		try
		{
			document.Rig.AuditIssues.RemoveAll( x =>
				x.Code is "arm_bone_collision" or "bind_pose_mismatch" );
			var collisions = HostSkeletonBuilder.FindArmBoneCollisions( document );
			foreach ( var collision in collisions )
			{
				document.Rig.AuditIssues.Add( new RigAuditIssue
				{
					Code = "arm_bone_collision",
					Message = $"Retained weapon bone '{collision}' conflicts with the Facepunch arm skeleton.",
					Severity = ValidationSeverity.Error,
					BoneName = collision
				} );
			}

			var parityIssues = HostSkeletonBuilder.ValidateBindParity( document );
			foreach ( var mismatch in parityIssues )
			{
				document.Rig.AuditIssues.Add( new RigAuditIssue
				{
					Code = "bind_pose_mismatch",
					Message =
						$"Bind pose mismatch for '{mismatch.BoneName}': "
						+ $"position {mismatch.PositionDelta:0.######}, "
						+ $"rotation {mismatch.RotationDelta:0.######}, "
						+ $"scale {mismatch.ScaleDelta:0.######}.",
					Severity = ValidationSeverity.Error,
					BoneName = mismatch.BoneName
				} );
			}

			if ( collisions.Count > 0 || parityIssues.Count > 0 )
			{
				document.Source.PreviewHostCompiled = false;
				var blockedDetail = collisions.Count > 0
					? $"Retained weapon bones collide with Facepunch bones: {string.Join( ", ", collisions )}."
					: document.Rig.AuditIssues.First( x => x.Code == "bind_pose_mismatch" ).Message;
				Log.Warning( $"[Weapon Animator] preview host blocked: {blockedDetail}" );
				return new PreviewHostResult
				{
					Success = false,
					Message = blockedDetail
				};
			}

			var cache = WeaponSourceImporter.GetPreviewCacheRoot( document.DocumentId );
			Directory.CreateDirectory( cache );
			var skeleton = HostSkeletonBuilder.Build( document );
			var dmxAbsolute = Path.Combine( cache, "animation_host_reference.dmx" );
			var vmdlAbsolute = Path.Combine( cache, "animation_host_preview.vmdl" );
			var dmxRelative = WeaponSourceImporter.RelativeAssetPath( dmxAbsolute );

			AtomicFile.WriteAllText( dmxAbsolute, DmxWriter.WriteReference( skeleton ) );
			AtomicFile.WriteAllText(
				vmdlAbsolute,
				ModelDocWriter.WriteHost(
					dmxRelative,
					[],
					"",
					skeleton.Bones.Select( bone => bone.Name ) ) );

			var asset = AssetSystem.RegisterFile( vmdlAbsolute );
			var compileReturned = asset?.Compile( true ) == true;
			var compiled = asset is not null
				&& asset.IsCompiled
				&& asset.IsCompiledAndUpToDate;
			var model = compiled ? asset!.LoadResource<Model>() : null;
			var success = compiled && model is not null && !model.IsError && model.BoneCount == skeleton.Bones.Count;
			var detail = DescribeResult(
				asset,
				compileReturned,
				compiled,
				model,
				skeleton.Bones.Count );

			document.Source.PreviewHostPath = asset?.Path ?? "";
			document.Source.PreviewHostCompiled = success;
			if ( !success )
				Log.Warning( $"[Weapon Animator] preview host verification failed: {detail}" );
			return new PreviewHostResult
			{
				Success = success,
				ModelPath = asset?.Path ?? "",
				Message = success
					? $"Preview host compiled with {skeleton.Bones.Count} bones."
					: $"Preview host verification failed: {detail}"
			};
		}
		catch ( Exception ex )
		{
			document.Source.PreviewHostCompiled = false;
			Log.Error( $"[Weapon Animator] preview host build failed: {ex}" );
			return new PreviewHostResult
			{
				Success = false,
				Message = ex.Message
			};
		}
	}

	private static string DescribeResult(
		Asset? asset,
		bool compileReturned,
		bool compiled,
		Model? model,
		int expectedBones )
	{
		if ( asset is null )
			return "the generated VMDL was not registered with the Asset System.";
		if ( !compiled )
		{
			return $"compile returned {compileReturned}, IsCompiled={asset.IsCompiled}, "
				+ $"IsCompiledAndUpToDate={asset.IsCompiledAndUpToDate}.";
		}
		if ( model is null )
			return "the compiled resource could not be loaded as a model.";
		if ( model.IsError )
			return "the compiled resource reloaded as the error model.";
		if ( model.BoneCount != expectedBones )
			return $"expected {expectedBones} bones but the compiled model exposes {model.BoneCount}.";
		return "the compiled resource did not pass validation.";
	}
}

public static class AtomicFile
{
	public static void WriteAllText( string path, string content )
	{
		var directory = Path.GetDirectoryName( path );
		if ( !string.IsNullOrWhiteSpace( directory ) )
			Directory.CreateDirectory( directory );

		var temporary = path + $".tmp.{Guid.NewGuid():N}";
		File.WriteAllText( temporary, content, new System.Text.UTF8Encoding( false ) );
		File.Move( temporary, path, true );
	}
}
