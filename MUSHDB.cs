using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SharpMUSH.DB;
using SQLitePCL;


namespace SharpMUSH
{

    public static class MUSHDB
    {

        public static async Task<bool> SetUserGuidAsync(int Id, Guid guid)
        {
            try
            {
                using (var Context = new MUSHContext())
                {
                    var user = await Context.Users.FindAsync(Id);
                    if (user != null)
                    {
                        user.Session = guid;
                        user.Connected = true;
                        Context.Update(user);
                        await Context.SaveChangesAsync();
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        public static async Task SetUserDisconnected(int Id)
        {
            try
            {
                using (var Context = new MUSHContext())
                {
                    var user = await Context.Users.FindAsync(Id);
                    if (user != null)
                    {

                        user.Session = Guid.Empty;
                        user.Connected = false;
                        Context.Update<UserType>(user);
                        await Context.SaveChangesAsync();

                    }
                }
            }
            catch
            {

            }

        }

        public static async Task<UserType> GetUserByIdAsync(int ThingID)
        {
            using (var Context = new MUSHContext())
            {
                try
                {
                    var thing = await Context.Users.Include(u => u.Location).FirstOrDefaultAsync(u => u.ThingID == ThingID);
                    
                    return thing;
                }
                catch
                {
                    return null;
                }
            }


        }

        public static async Task<int> GetUserIDByGuidAsync(Guid guid)
        {
            try
            {

                using (var Context = new MUSHContext())
                {
                    var thing = await Context.Users.FirstAsync(u => u.Session == guid);
                    
                    return thing.ThingID;
                }
            }
            catch
            {
                return -1;
            }
        }


        public static async Task<UserType[]> GetUsersInLocationByIdAsync(int locId)
        {
            try
            {
                await using (var Context = new MUSHContext())
                {
                    
                    return await Context.Users.Where(u => u.Location.ThingID == locId).ToArrayAsync();
                    
                }
            }
            catch
            {
                return null;
            }
        }

        public static async Task<int> AuthUserByNameAsync(string name, string pass, Guid guid)
        {
            try
            {
                using (var Context = new MUSHContext())
                {

                    var user = await Context.Users.FirstAsync(u => u.Name == name && u.Password == pass);

                    SetUserGuidAsync(user.ThingID, guid);
                    return user.ThingID;
                }
            }
            catch
            {

                return -1;
            }
        }

        public static async Task<RoomType> getRoomByIdAsync(int locationThingId)
        {
            using (var Context = new MUSHContext())
            {
                var room = await Context.Rooms.Include(r => r.Attributes).Include(r => r.Flags)
                    .FirstAsync(r => r.ThingID == locationThingId);

                return room;
            }
        }

        public static async Task<ThingType[]> GetThingsInLocationByIdAsync(int roomThingId)
        {
            try
            {
                await using (var Context = new MUSHContext())
                {

                    return await Context.Things.Where(t => t.Location.ThingID == roomThingId).ToArrayAsync();

                }
            }
            catch
            {
                return null;
            }
        }
    }
}
