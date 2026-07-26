#nullable enable annotations

using Sandbox;

namespace SboxWeaponAnimator.Editor;

public static class CalibrationBindingSeeder
{
	public static bool SeedDefaultPrimaryHand( WeaponAnimationDocument document )
	{
		var grip = document.Calibration.GetAnchor( AnchorKind.Grip );
		if ( grip is null )
			return false;

		var placement = WeaponAnimationMath.Compose(
			document.Calibration.PhysicalTransform,
			document.Calibration.FramingTransform );
		var worldGrip = new Transform(
			placement.PointToWorld( grip.LocalPosition ),
			placement.Rotation * grip.LocalRotation );
		var skeleton = HostSkeletonBuilder.Build( document, includeArmProfile: false );
		var attachment = document.Binding.PrimaryHand.AttachedBone;
		if ( string.IsNullOrWhiteSpace( attachment )
			|| !skeleton.ByName.TryGetValue( attachment, out var attachedBone )
			|| !attachedBone.IsWeaponBone )
		{
			attachment = "weapon_root";
			skeleton.ByName.TryGetValue( attachment, out attachedBone );
		}

		document.Binding.PrimaryHand.Transform = attachedBone is null
			? worldGrip
			: attachedBone.BindModelTransform.ToLocal( worldGrip );
		document.Binding.PrimaryHand.AttachedBone = attachedBone is null ? "" : attachment;
		document.Binding.PrimaryHand.IsBound = false;
		return true;
	}
}
