using System;
using System.Collections.Generic;
using System.Linq;
using NightDock.Sim;

/// <summary>
/// Systems Designer's balance check: bots play a career of shifts so we can see whether a shift is
/// "tight but winnable". Not part of the Unity game.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        var b = new Balance();
        Console.WriteLine("Night Dock balance check (game minutes, $)");
        foreach (var style in new[] { "careful", "sloppy" })
        {
            foreach (int forklifts in new[] { 2, 3, 4 })
            {
                foreach (bool scanner in new[] { false, true })
                {
                    Console.Write($"{style,-8} forklifts={forklifts} scanner={(scanner ? "y" : "n")} | ");
                    var parts = new List<string>();
                    for (int shift = 1; shift <= 6; shift++)
                    {
                        var c = Career.New(b, 1000 + shift);
                        c.ShiftNumber = shift; c.Forklifts = forklifts; c.FastScanner = scanner;
                        var sim = new ShiftSim(b, c);
                        Play(sim, style == "careful");
                        var r = sim.Report;
                        parts.Add($"S{shift}:{r.OnTime}/{r.TotalPallets} lb{r.LeftBehind} lo{r.LoadOrderViolations} ${r.Profit} {r.Stars}*");
                    }
                    Console.WriteLine(string.Join("  ", parts));
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("Career (careful bot buys forklift, then scanner, then door when affordable):");
        var career = Career.New(b, 77);
        for (int i = 0; i < 10 && !career.Bankrupt; i++)
        {
            var sim = new ShiftSim(b, career);
            Play(sim, true);
            var r = sim.Report;
            career.Apply(b, r);
            string bought = "";
            if (career.BuyForklift(b)) bought += " +forklift";
            else if (career.BuyScanner(b)) bought += " +scanner";
            else if (career.BuyDoor(b, 0)) bought += " +door";
            Console.WriteLine($"  shift {r.ShiftNumber}: {r.TotalPallets} pallets, on time {r.OnTime}, left {r.LeftBehind}, order {r.LoadOrderViolations}, profit ${r.Profit}, {r.Stars}*, cash ${career.Cash}{bought}");
        }
        return 0;
    }

    /// <summary>
    /// careful: loads a pallet straight onto a trailer only if that keeps last-stop-first; otherwise stages it,
    /// and pulls staged pallets onto trailers highest stop first. sloppy: everything straight to its route's door.
    /// </summary>
    static void Play(ShiftSim s, bool careful)
    {
        while (!s.Ended)
        {
            ShiftBot.Think(s, careful);
            s.Tick(1f);
        }
    }
}
