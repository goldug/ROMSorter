﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RomDatabase5
{
    public class DatabaseEntities //EF access to database. Much less code, sometimes faster.
    {
        //Entity types are plural, my handmane ones are singular.       

        RomDBContext db = new RomDBContext();
        public ILookup<long, string> consoleIDs;

        public DatabaseEntities()
        {
            consoleIDs = db.Consoles.ToLookup(k => k.Id, v => v.Name);
        }

        public void InsertGame(RomDatabase5.Games g)
        {
            db.Games.Add(g);
            db.SaveChanges();
        }

        public void InsertGamesBatch(List<Games> games)
        {
            db.Games.AddRange(games);
            db.SaveChanges();
        }

        public void InsertDisc(RomDatabase5.Discs g)
        {
            db.Discs.Add(g);
            db.SaveChanges();
        }

        public void InsertDiscsBatch(List<Discs> games)
        {
            db.Discs.AddRange(games);
            db.SaveChanges();
        }

        public int CountGames()
        {
            int gameCount = db.Games.Count();
            int discCount = db.Discs.Select(d => d.Name).Distinct().Count();
            return gameCount + discCount;
        }

        public List<Tuple<string, int>> CountGamesByConsole()
        {
            var consoles = db.Consoles.ToLookup(k => k.Id, v => v.Name);
            var results = db.Games.GroupBy(g => g.Console).Select(g => new { Key = consoles[g.Key.Value].First(), Count = g.Count() }).ToList();
            var results2 = db.Discs.GroupBy(g => g.Console).Select(g => new { Key = consoles[g.Key.Value].First(), Count = g.Count() }).ToList();

            List<Tuple<string, int>> finalResults = new List<Tuple<string, int>>();

            foreach (var console in results)
            {
                if (results2.Any(r => r.Key == console.Key))
                {
                    finalResults.Add(new Tuple<string, int>(console.Key, console.Count + results2.Where(r => r.Key == console.Key).First().Count));
                }
                else
                {
                    finalResults.Add(new Tuple<string, int>(console.Key, console.Count));
                }
            }
            foreach (var console2 in results2)
            {
                if (!finalResults.Any(r => r.Item1 == console2.Key)) //don't re-add results we already put in.
                {
                    finalResults.Add(new Tuple<string, int>(console2.Key, console2.Count));
                }
            }

            return finalResults.OrderBy(fr => fr.Item1).ToList();
        }

        public Games FindGame(long size, string[] hashes)
        {
            return FindGame(size, hashes[2], hashes[0], hashes[1]);
        }

        public Games FindGame(long size, string crc, string md5, string sha1)
        {
            //Potentially necessary future planning TODO:
            //get all games by CRC, then search by SHA1, then MD5 (Pinball dat files don't have MD5s, so this won't find them   
            Games g = db.Games.Where(g => g.Size == size && g.Crc == crc && g.Md5 == md5 && g.Sha1 == sha1).FirstOrDefault();
            return g;
        }

        public List<Discs> FindDisc(long size, string[] hashes)
        {
            return FindDisc(size, hashes[2], hashes[0], hashes[1]);
        }

        public List<Discs> FindDisc(long size, string crc, string md5, string sha1)
        {
            List<Discs> d = db.Discs.Where(g => g.Size == size && g.Crc == crc && g.Md5 == md5 && g.Sha1 == sha1).ToList();
            return d;
        }

        public List<Games> GetAllGames()
        {
            return db.Games.ToList();
        }

        public List<Games> GetGamesByConsole(string console)
        {
            var consoleID = db.Consoles.Where(c => c.Name == console).FirstOrDefault().Id;
            return db.Games.Where(g => g.Console == consoleID).ToList();
        }

    }
}