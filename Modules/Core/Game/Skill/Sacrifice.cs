static partial class Game
{
    // Owned by one original player cast, shared by its repeats/cascades; never created by a proxy executor.
    internal sealed class SacrificeUse(ClientState caster, ushort skill, int effect, uint gem)
    {
        readonly int epoch = caster.ProjectileEpoch;
        readonly GameServer.Modding.IModuleLifetime? owner = CombatLifetime;
        int? added;

        internal bool Current => CurrentGameWork && ReferenceEquals(owner, CombatLifetime)
            && caster.ProjectileActive(epoch)
            && GemSkillEffects(caster).Any(g => g.Id == skill && g.Effect == effect && g.Gem == gem)
            && GemSkillInstanceDisabled(caster, effect, skill) is null;

        internal int AddedChaos()
        {
            lock (caster.Monsters)
            {
                if (!Current) return 0;
                if (added is int captured) return captured;
                var supported = SupportedSkill(caster, skill);
                int Get(string name) => Stats.Key(name) is uint key ? supported.Stats.GetValueOrDefault(key) : 0;
                int percent = Get("support_sacrifice_sacrifice_%_of_current_life");
                int sacrificed = SkillsFor(caster).IsSpell(effect) && percent > 0 && caster.PlayerLife > 1
                    ? (int)Math.Min((long)caster.PlayerLife - 1, (long)caster.PlayerLife * percent / 100) : 0;
                caster.PlayerLife -= (uint)sacrificed;
                added = (int)((long)sacrificed * Get("support_sacrifice_gain_%_of_sacrificed_life_as_added_chaos_damage") / 100);
                return added.Value;
            }
        }
    }
}
