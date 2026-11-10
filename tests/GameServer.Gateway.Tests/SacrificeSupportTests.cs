using GameServer.Core.Protocol.Instance;
using GameServer.Core.Projectiles;
using GameServer.Core.World;
using GameServer.Modding;
using League = GameServer.Core.Accounts.League;

namespace GameServer.Gateway.Tests;

public class SacrificeSupportTests
{
    [Fact]
    public async Task Projectile_cast_sacrifices_once_at_release_and_added_chaos_uses_common_damage_rules()
    {
        using var fixture = new Fixture();
        foreach (string spell in new[] { "Fireball", "Spark" })
        {
            fixture.Equip(spell, echo: true);
            var conn = fixture.Conn;
            var gem = Game.GemSkillEffects(conn).First(g => g.Gem != 0 && (spell == "Reap" || Game.SkillsFor(conn).EffectId(g.Effect) == spell));
            var session = fixture.Session;
            await Game.LaunchProjectile("sacrifice", null!, null!, conn, gem.Id, (100,100), (120,100), conn.ActionInterruptionGeneration);
            Assert.Equal(1000u, conn.PlayerLife);
            session.AdvanceTo(ProjectileRuntime.Time(.6));
            Assert.Equal(800u, conn.PlayerLife);
            var hit = Assert.IsType<ProjectileOffense>(session.Runtime.Projectiles.First().Use.Payload).Hit;
            Assert.True(hit[DamageType.Chaos].Min > 0);
            session.AdvanceTo(ProjectileRuntime.Time(1.5));
            Assert.Equal(800u, conn.PlayerLife);

            // A separate original cast pays again, with live life, and shares increased/more and prohibitions.
            fixture.ResetSession();
            conn.PlayerStats[Program.Stats.Key("chaos_damage_+%")!.Value] = 200;
            await Game.LaunchProjectile("sacrifice", null!, null!, conn, gem.Id, (100,100), (120,100), conn.ActionInterruptionGeneration);
            fixture.Session.AdvanceTo(ProjectileRuntime.Time(.6));
            var increased = Assert.IsType<ProjectileOffense>(fixture.Session.Runtime.Projectiles.First().Use.Payload).Hit;
            Assert.Equal(hit[DamageType.Chaos].Min * 3, increased[DamageType.Chaos].Min, 6);
            fixture.ResetSession();
            conn.PlayerStats[Program.Stats.Key("base_deal_no_chaos_damage")!.Value] = 1;
            await Game.LaunchProjectile("sacrifice", null!, null!, conn, gem.Id, (100,100), (120,100), conn.ActionInterruptionGeneration);
            fixture.Session.AdvanceTo(ProjectileRuntime.Time(.6));
            Assert.Equal(0, Assert.IsType<ProjectileOffense>(fixture.Session.Runtime.Projectiles.First().Use.Payload).Hit[DamageType.Chaos].Min);
            conn.PlayerStats[StatKeys.SpellSkillsDealNoDamage] = 1;
            Assert.Equal(0, Game.SupportedHitFor(conn, gem.Id, suppress: true, addedChaos: 70).MinTotal);
            conn.PlayerStats.Clear();
        }
    }

    [Fact]
    public async Task Cancelled_or_removed_sources_never_sacrifice_player_life()
    {
        using var fixture = new Fixture();
        foreach (string spell in new[] { "Fireball", "Spark", "Reap" })
        {
            fixture.Equip(spell);
            var conn = fixture.Conn;
            var gem = Game.GemSkillEffects(conn).First(g => g.Gem != 0 && (spell == "Reap" || Game.SkillsFor(conn).EffectId(g.Effect) == spell));
            await Game.LaunchProjectile("cancelled", null!, null!, conn, gem.Id, (100,100), (120,100), conn.ActionInterruptionGeneration, actionToken: conn.ActionCount - 1);
            fixture.Session.AdvanceTo(ProjectileRuntime.Time(2));
            Assert.Equal(1000u, conn.PlayerLife);
            Assert.Empty(fixture.Session.Runtime.Projectiles);
            if (spell == "Reap") continue;
            fixture.ResetSession();
            await Game.LaunchProjectile("removed", null!, null!, conn, gem.Id, (100,100), (120,100), conn.ActionInterruptionGeneration);
            Game.ItemsOf(conn).Remove(PlayerInventories.Weapon1, fixture.Host);
            fixture.Session.AdvanceTo(ProjectileRuntime.Time(2));
            Assert.Equal(1000u, conn.PlayerLife);
            Assert.Empty(fixture.Session.Runtime.Projectiles);
        }
    }

    sealed class Fixture : IDisposable
    {
        readonly string path = Path.Combine(Path.GetTempPath(), $"sacrifice-{Guid.NewGuid():N}.db");
        readonly IGameLogic? previous = Program.GameLogicOrNull;
        readonly IModuleLifetime? previousLifetime = Game.CombatLifetime;
        readonly ModuleLifetime lifetime = new("Game");
        public ClientState Conn { get; }
        public Game.ProjectileSession Session { get; private set; } = null!;
        public uint Host;
        public Fixture()
        {
            Saves.Open(path);
            Game.CombatLifetime = lifetime;
            Program.SetGameLogic(new GameServer.Modules.Game.GameLogic());
            Conn = new(new(1, "sacrifice", 1000,1000,100,100,null,[]),1,1)
            {
                Character = "Sacrifice" + Guid.NewGuid().ToString("N"), PlayerLife = 1000, PlayerMana = 1000, Instance = new(),
                Experience = CharacterStats.ExperienceFor(100),
                Pools = new(100,1000,1000,int.MaxValue,0,Strength:1000,Dexterity:1000,Intelligence:1000),
                LeagueModule = new(new League("test","Standard",false,false),new HashSet<string>(["Game"]))
            };
            Conn.Clock.Stop();
            ResetSession();
        }
        public void ResetSession()
        {
            Session?.Invalidate();
            Conn.PlayerLife = 1000;
            Session = new("sacrifice",null!,null!,Conn);
            Conn.ProjectileRuntime = Session;
        }
        public void Equip(string spell, bool echo = false)
        {
            if (Host != 0) Game.ItemsOf(Conn).Remove(PlayerInventories.Weapon1, Host);
            ResetSession();
            var sockets = new List<ItemSocket>();
            foreach (string id in echo ? new[] { "SkillGem" + (spell == "Reap" ? "Bloodreap" : spell), "SupportGemSacrifice", "SupportGemSpellEcho" }
                                      : new[] { "SkillGem" + (spell == "Reap" ? "Bloodreap" : spell), "SupportGemSacrifice" })
                sockets.Add(new(SocketColour.White,ItemCatalog.Create(Program.Items.Find("Metadata/Items/Gems/" + id)!)));
            var wand = ItemCatalog.Create(Program.Items.Find("Driftwood Wand")!) with { Mods = [], Sockets = sockets.ToArray(), Links = [(byte)sockets.Count] };
            Host = Game.ItemsOf(Conn).Add(PlayerInventories.Weapon1,0,0,wand).Id;
            foreach (string name in new[] { "virtual_strength_for_requirements", "virtual_dexterity_for_requirements", "virtual_intelligence_for_requirements" })
                Conn.PlayerStats[Program.Stats.Key(name)!.Value] = 1000;
        }
        public void Dispose()
        {
            Session.Invalidate(); lifetime.Stop(ex => throw ex); Game.CombatLifetime = previousLifetime; PlayerInventories.Forget(Conn.Character!); Program.SetGameLogic(previous!); TestEnvironment.ReleaseSaves(path);
        }
    }
}




