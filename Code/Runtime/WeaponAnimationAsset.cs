#nullable enable annotations

using Sandbox;

namespace SboxWeaponAnimator;

[AssetType(
	Name = "Weapon Animation Project",
	Extension = "wepanim",
	Category = "Animation",
	Flags = AssetTypeFlags.NoEmbedding )]
public sealed class WeaponAnimationAsset : GameResource
{
	[Property, Hide]
	public WeaponAnimationDocument Document { get; set; } = WeaponAnimationDocument.CreateDefault();
}
