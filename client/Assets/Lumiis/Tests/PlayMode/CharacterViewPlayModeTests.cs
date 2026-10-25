using System.Collections;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Lumiis.Client.Tests.PlayMode
{
    /// <summary>PlayMode-Tests für den Paper-Doll-/Socketing-Umbau: Hierarchie, Waffenstufen-VFX,
    /// Rüstungs-Palette per MaterialPropertyBlock und der an die Schlagframes gekoppelte Trail. Diese Dinge
    /// leben ausschließlich in UnityEngine-Komponenten und sind deshalb nicht über `dotnet test` prüfbar.</summary>
    public class CharacterViewPlayModeTests
    {
        private const string LookJson = @"{
  ""settings"": { ""canvasPx"": 128, ""ppu"": 64 },
  ""hairColors"": [""#111111"", ""#222222""],
  ""looks"": [
    { ""id"": 1, ""slot"": ""Body"", ""name"": ""Basis"",
      ""anchors"": {
        ""down"": { ""hand"": [20, 70], ""head"": [0, 100] },
        ""up"": { ""hand"": [-20, 70], ""head"": [0, 100] },
        ""side"": { ""hand"": [10, 70], ""head"": [0, 100] }
      } },
    { ""id"": 1, ""slot"": ""Hair"", ""name"": ""Kurz"" },
    { ""id"": 1, ""slot"": ""Armor"", ""name"": ""Stoff"",
      ""colors"": { ""primaryColor"": ""#8a6a45"", ""secondaryColor"": ""#5a4229"", ""accentColor"": ""#c9a86a"" } },
    { ""id"": 2, ""slot"": ""Armor"", ""name"": ""Leder"",
      ""colors"": { ""primaryColor"": ""#6b4a2f"", ""secondaryColor"": ""#3f2a18"", ""accentColor"": ""#b98a4f"" } },
    { ""id"": 1, ""slot"": ""Helmet"", ""name"": ""Kappe"" },
    { ""id"": 2, ""slot"": ""Weapon"", ""name"": ""Schwert"" },
    { ""id"": 1, ""slot"": ""Shield"", ""name"": ""Rundschild"" }
  ]
}";

        private GameObject _root;
        private CachingSpriteProvider _sprites;
        private LookCatalogClient _lookCatalog;
        private CharacterView _view;

        [SetUp]
        public void SetUp()
        {
            _lookCatalog = new LookCatalogClient(LookJson);
            _sprites = new CachingSpriteProvider(new PlaceholderSpriteProvider(_lookCatalog.CanvasPx, _lookCatalog.Ppu));

            _root = new GameObject("CharacterViewTest");
            _view = _root.AddComponent<CharacterView>();
            _view.Initialize(1, "Test", _sprites, _lookCatalog);
            _view.SetAppearance(new Appearance
            {
                BodyId = 1,
                HairStyleId = 1,
                HairColorId = 1,
                ArmorId = 2,
                WeaponId = 2
            });
            _view.SetFacing(new Facing(FacingDirection.Down, false));
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            _sprites?.DestroyAll();
        }

        [Test]
        public void Hierarchy_IsBodySocketWeapon()
        {
            Assert.IsNotNull(_view.BodyRenderer);
            Assert.IsNotNull(_view.WeaponSocket);
            Assert.IsNotNull(_view.WeaponRenderer);

            Assert.AreSame(_view.BodyRenderer.transform, _view.WeaponSocket.parent);
            Assert.AreSame(_view.WeaponSocket, _view.WeaponRenderer.transform.parent);
        }

        [Test]
        public void ArmorMaterial_IsAvailableInResources()
        {
            Material material = Resources.Load<Material>("Materials/CharacterArmor");
            Assert.IsNotNull(material, "CharacterArmor.mat fehlt unter Assets/Resources/Materials/.");
            Assert.AreEqual("Lumiis/CharacterArmor", material.shader.name);
        }

        [Test]
        public void AssetLabUi_AndWeaponSprites_AreAvailable()
        {
            Assert.IsNotNull(Resources.Load<VisualTreeAsset>("UI/AssetTestLab"), "AssetTestLab.uxml fehlt unter Assets/Resources/UI/.");

            Sprite ironSword = Resources.Load<Sprite>("Sprites/Items/weapon_2_iron");
            Assert.IsNotNull(ironSword, "weapon_2_iron.png wurde nicht als Sprite importiert.");
            Assert.AreEqual(64f, ironSword.pixelsPerUnit, 0.01f);
            Assert.AreEqual(0.5f, ironSword.pivot.x / ironSword.rect.width, 0.01f);
            Assert.AreEqual(0.125f, ironSword.pivot.y / ironSword.rect.height, 0.01f);
        }

        [Test]
        public void LumiisArchive_IconManifestAndCharacterSheets_AreAvailable()
        {
            ItemIconCatalog icons = ItemIconCatalog.LoadFromResources();
            Assert.Greater(icons.All.Count, 500, "Icon-Manifest wurde nicht oder unvollständig importiert.");
            Assert.IsNotNull(icons.GetTexture("sword_30"), "sword_30-Icon fehlt.");
            Assert.IsNotNull(icons.GetTexture("red_s"), "red_s-Trank-Icon fehlt.");

            Assert.IsNotNull(Resources.Load<Texture2D>("Characters/body/warrior_m"), "Krieger-Body-Sheet fehlt.");
            Assert.IsNotNull(Resources.Load<Texture2D>("Characters/weapon/schwert_02"), "Schwert-Sheet fehlt.");
        }

        [Test]
        public void Map1AndMonsterArt_AreImported()
        {
            Sprite map = Resources.Load<Sprite>("Maps/map1");
            Assert.IsNotNull(map, "map1.png fehlt unter Resources/Maps/ oder wurde nicht als Sprite importiert.");
            Assert.AreEqual(128f, map.rect.width / map.pixelsPerUnit, 0.01f);
            Assert.AreEqual(128f, map.rect.height / map.pixelsPerUnit, 0.01f);

            Sprite[] wolf = MonsterArt.LoadFrames("alpha_wolf");
            Assert.IsNotNull(wolf, "alpha_wolf.png fehlt unter Resources/Sprites/Monsters/.");
            Assert.AreEqual(20, wolf.Length);
            Assert.AreEqual(1.25f, wolf[0].rect.width / wolf[0].pixelsPerUnit, 0.01f);
            Assert.IsNotNull(MonsterArt.LoadFrames("boar"));
            Assert.IsNotNull(ItemIconCatalog.LoadFromResources().GetTexture("book_bash"), "book_bash-Icon fehlt.");
        }

        [Test]
        public void SetWeapon_TogglesPlus7AndPlus9GlowByUpgradeLevel()
        {
            _view.SetWeapon(null, Vector2.zero, 6);
            Assert.IsFalse(_view.IsPlus7GlowActive);
            Assert.IsFalse(_view.IsPlus9GlowActive);

            _view.SetWeapon(null, Vector2.zero, 7);
            Assert.IsTrue(_view.IsPlus7GlowActive);
            Assert.IsFalse(_view.IsPlus9GlowActive);

            _view.SetWeapon(null, Vector2.zero, 9);
            Assert.IsTrue(_view.IsPlus7GlowActive);
            Assert.IsTrue(_view.IsPlus9GlowActive);

            _view.ClearWeapon();
            Assert.IsFalse(_view.IsPlus7GlowActive);
            Assert.IsFalse(_view.IsPlus9GlowActive);
        }

        [Test]
        public void SetAppearance_ArmorColors_AreWrittenToMaterialPropertyBlock()
        {
            var block = new MaterialPropertyBlock();
            _view.BodyRenderer.GetPropertyBlock(block);

            AssertColor(new Color32(0x6b, 0x4a, 0x2f, 0xff), block.GetColor(Shader.PropertyToID("_Color_ArmorPrimary")));
            AssertColor(new Color32(0x3f, 0x2a, 0x18, 0xff), block.GetColor(Shader.PropertyToID("_Color_ArmorSecondary")));
            AssertColor(new Color32(0xb9, 0x8a, 0x4f, 0xff), block.GetColor(Shader.PropertyToID("_Color_ArmorTrim")));
        }

        [UnityTest]
        public IEnumerator PlayAttack_EnablesTrailOnlyDuringActiveAttackFrames()
        {
            var animator = _root.AddComponent<CharacterAnimator>();
            animator.Initialize(_view);

            Assert.IsFalse(_view.IsAttackTrailEmitting, "Trail darf im Idle nicht emittieren.");

            animator.PlayAttack();
            animator.Animate(isMoving: false); // Frame 0: noch nicht aktive Schlagphase
            Assert.IsFalse(_view.IsAttackTrailEmitting);

            yield return new WaitForSeconds(0.12f);
            animator.Animate(isMoving: false); // Frame 1/2: aktive Schlagphase
            Assert.IsTrue(_view.IsAttackTrailEmitting);

            yield return new WaitForSeconds(0.3f);
            animator.Animate(isMoving: false); // Schlag vorbei -> Idle
            Assert.IsFalse(_view.IsAttackTrailEmitting);
        }

        private static void AssertColor(Color expected, Color actual)
        {
            Assert.AreEqual(expected.r, actual.r, 0.01f);
            Assert.AreEqual(expected.g, actual.g, 0.01f);
            Assert.AreEqual(expected.b, actual.b, 0.01f);
            Assert.AreEqual(expected.a, actual.a, 0.01f);
        }
    }
}
