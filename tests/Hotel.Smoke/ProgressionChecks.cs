using System.Numerics;
using Hotel.Game.Combat;
using Hotel.Game.Expedition;
using Hotel.Game.Player;
using Hotel.Game.Progression;
using Hotel.Game.Residents;
using Hotel.Game.Scene;
using Hotel.Game.Supplies;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using ItemDefinition = Hotel.Game.Supplies.ItemDefinition;

// Progression: landed hits practise their skills, a skill's rank adds its attributes and hit contributions as Engine
// sources, felling a resident gives experience and levels cross their thresholds, relics add permanent sources and tomes
// practice, and a capture restores all of it whole.
internal static class ProgressionChecks
{
    internal static void Run(IEngineContext engine)
    {
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        var content = Owners.Content(engine);
        GrowthDefinition growth = content.Growth;
        using HotelScene scene = Owners.Scene(engine, content);
        using HotelPlayer player = Owners.Player(engine, scene, content);
        HotelSupplies supplies = Owners.Supplies(content, scene.PlayerEntity, 12);
        InvestigatorGrowth grown = supplies.Growth;
        ResidentKind still = content.Combat.Residents.Single(r => r.Id == "porter") with
        {
            Id = "still", Movement = new(0, 0, Post: new()), Perception = new(0.1f, 1, 0, 0)
        };
        HotelCombat combat = new(engine, scene, player, supplies, content.Combat with { Residents = [.. content.Combat.Residents, still] },
            [new ResidentPlacement("still", "still", [0, .9f, -9])]);
        HotelEnemy target = combat.Enemies.Single();
        player.Reset();
        scene.Entities.Set(scene.PlayerEntity, EngineComponentTypes.Transform, new Transform(new(0, .875f, -7.9f), Quaternion.Identity, Vector3.One));
        void Steps(int frames) { for (int i = 0; i < frames; i++) combat.Step(1f / 60); }
        bool Explains(string stat, string source) => supplies.Stats.Explain(stat).Decisions.Any(d =>
            d.Outcome == MechanicsDecisionOutcome.Applied && d.SourceDefinition.Value == source);
        int Swing() { int before = target.Health.ValueInt; Check(combat.Act(false), "the pry bar swings"); Steps(90); return before - target.Health.ValueInt; }

        // Growth by use: each landed blunt hit is one use of Striking; the first rank adds might and a damage contribution.
        Check(grown.Level == 1 && grown.Uses("striking") == 0 && !grown.Contributions.Any(), "a run starts at the first level, unpractised");
        double might = supplies.Stats.Stat("might").Value;
        int plain = Swing();
        Check(plain > 0 && grown.Uses("striking") == 1 && grown.Uses("marksmanship") == 0, $"a landed blunt hit is one use of striking: {grown.Uses("striking")}");
        SkillDefinition striking = growth.Skill("striking")!;
        grown.Learn("drill", new([], new() { ["striking"] = striking.Ranks[0] - 1 }));
        Check(grown.Rank("striking") == 1 && Math.Abs(supplies.Stats.Stat("might").Value - might - striking.PerRank[0].Amount) < 1e-6 &&
            Explains("might", "growth.skill.striking") && grown.Contributions.Count() == striking.Contributions.Length,
            "the first rank of striking adds might and its contribution, as an Engine source that explains itself");
        target.Reset();
        int practised = Swing();
        Check(practised > plain, $"a practised blow lands harder: {plain} then {practised}");

        // Experience: felling a resident gives its kind's experience; levels cross their thresholds.
        target.Reset();
        target.Stats.Track("health").SetCurrent(1, false);
        Swing();
        Check(!target.Alive && grown.Experience == still.Experience, $"felling the resident gives its experience: {grown.Experience}");
        // A resident felled by an admitted tick (a burn) gives its experience too, once.
        target.Reset();
        target.Stats.Track("health").SetCurrent(1, false);
        int before = grown.Experience;
        target.Stats.Effects.Apply(content.Mechanics.Effect("burning")!, "action.flare-shot");
        Steps(600);
        Check(!target.Alive && grown.Experience == before + still.Experience, $"a burn that fells the resident gives its experience once: {grown.Experience}");
        int second = growth.Levels.Experience[1];
        int health = supplies.MaximumHealth;
        grown.Award(second - 1 - grown.Experience);
        Check(grown.Level == 1 && supplies.MaximumHealth == health, "one short of the threshold is still the first level");
        grown.Award(1);
        float perLevel = growth.Levels.PerLevel.Single(s => s.Stat == "maximum-health").Amount;
        Check(grown.Level == 2 && supplies.MaximumHealth == health + (int)perLevel && Explains("maximum-health", "growth.level"),
            $"the threshold reaches the second level and its health: {supplies.MaximumHealth}");

        // Relics and tomes: used from the case, a relic adds permanent stats; a tome practises its skill until mastered.
        might = supplies.Stats.Stat("might").Value;
        ItemDefinition medal = supplies.Item("service-medal");
        Check(supplies.Give("service-medal", 1) && supplies.Use(supplies.PocketOf("relic"), supplies.Revision) && supplies.PocketOf("relic") < 0,
            "a relic is used up");
        Check(Math.Abs(supplies.Stats.Stat("might").Value - might - medal.Use!.Growth!.Stats.Single(s => s.Stat == "might").Amount) < 1e-6 &&
            Explains("might", "growth.relic.service-medal") && grown.Relics.SequenceEqual(["service-medal"]), "the relic's might stays as its own source");
        int uses = grown.Uses("striking");
        Check(supplies.Give("porters-handbook", 1) && supplies.Use(supplies.PocketOf("tome"), supplies.Revision) &&
            grown.Uses("striking") == uses + supplies.Item("porters-handbook").Use!.Growth!.Practice["striking"], "a tome practises its skill");
        grown.Learn("drill", new([], new() { ["striking"] = striking.Mastery }));
        Check(grown.Uses("striking") == striking.Mastery && grown.Rank("striking") == striking.Ranks.Length, "practice stops at mastery");
        Check(supplies.Give("porters-handbook", 1) && supplies.UseReason(supplies.PocketOf("tome")).Length > 0 &&
            !supplies.Use(supplies.PocketOf("tome"), supplies.Revision), "a tome with nothing left to teach is kept");

        // A capture restores growth whole, before the stats it feeds; impossible growth is refused.
        SuppliesState saved = supplies.Capture();
        HotelSupplies restored = Owners.Supplies(content, scene.PlayerEntity, 12);
        restored.Validate(saved);
        restored.Restore(saved);
        Check(restored.Growth.Level == grown.Level && restored.Growth.Experience == grown.Experience &&
            restored.Growth.Rank("striking") == grown.Rank("striking") && restored.Growth.Relics.SequenceEqual(grown.Relics) &&
            restored.Stats.Stat("might").Value == supplies.Stats.Stat("might").Value && restored.MaximumHealth == supplies.MaximumHealth &&
            restored.Health == supplies.Health, "a capture restores level, ranks, relics and the stats they give");
        foreach (GrowthState invalid in new[] {
            saved.Growth with { Experience = -1 },
            saved.Growth with { Uses = new() { ["juggling"] = 1 } },
            saved.Growth with { Uses = new() { ["striking"] = striking.Mastery + 1 } },
            saved.Growth with { Relics = ["bandage"] } })
        {
            bool refused = false;
            try { restored.Validate(saved with { Growth = invalid }); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "negative experience, an unknown skill, practice past mastery or a relic that is not one is refused");
        }
        restored.Reset();
        Check(restored.Growth.Level == 1 && restored.Growth.Relics.Count == 0 && !restored.Growth.Contributions.Any(), "a new run starts ungrown");
        Console.WriteLine($"Progression checks passed: {growth.Levels.Experience.Length} levels, {growth.Skills.Length} skills; growth by use, " +
            "ranks as Engine sources and hit contributions, experience thresholds, relics and tomes, and a round trip.");
    }
}
