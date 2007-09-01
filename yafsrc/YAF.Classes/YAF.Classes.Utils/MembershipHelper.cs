/* Yet Another Forum.net
 * Copyright (C) 2003-2005 Bj�rnar Henden
 * Copyright (C) 2006-2007 Jaben Cargman
 * http://www.yetanotherforum.net/
 * 
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
 */

using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Security;
using System.Web.Security;
using YAF.Classes.Data;

namespace YAF.Classes.Utils
{
  public class MembershipHelper
  {
    public static void SyncUsers( int PageBoardID )
    {
      string ForumURL = "forumurl";
      string ForumEmail = "forumemail";
      string ForumName = "forumname";

      using ( DataTable dt = YAF.Classes.Data.DB.user_list( PageBoardID, DBNull.Value, true ) )
      {
        foreach ( DataRow row in dt.Rows )
        {
          if ( ( int ) row ["IsGuest"] > 0 )
            continue;

          string name = ( string ) row ["Name"];

          MembershipUser user = Membership.GetUser( name );
          if ( user == null )
          {
            string password;
            MembershipCreateStatus status;
            int retry = 0;
            do
            {
              password = Membership.GeneratePassword( 7 + retry, 1 + retry );
              user = Membership.CreateUser( name, password, ( string ) row ["Email"], "-", ( string ) row ["Password"], true, out status );
            } while ( status == MembershipCreateStatus.InvalidPassword && ++retry < 10 );

            if ( status != MembershipCreateStatus.Success )
            {
              throw new ApplicationException( string.Format( "Failed to create user {0}: {1}", name, status ) );
            }
            else
            {
              user.Comment = "Copied from Yet Another Forum.net";
              Membership.UpdateUser( user );

              /// Email generated password to user
              System.Text.StringBuilder msg = new System.Text.StringBuilder();
              msg.AppendFormat( "Hello {0}.\r\n\r\n", name );
              msg.AppendFormat( "Here is your new password: {0}\r\n\r\n", password );
              msg.AppendFormat( "Visit {0} at {1}", ForumName, ForumURL );

              YAF.Classes.Data.DB.mail_create( ForumEmail, user.Email, "Forum Upgrade", msg.ToString() );
            }
          }
          YAF.Classes.Data.DB.user_migrate( row ["UserID"], user.ProviderUserKey );


          using ( DataTable dtGroups = YAF.Classes.Data.DB.usergroup_list( row ["UserID"] ) )
          {
            foreach ( DataRow rowGroup in dtGroups.Rows )
            {
              Roles.AddUserToRole( user.UserName, rowGroup ["Name"].ToString() );
            }
          }
        }
      }
    }

    static private bool BitSet( object _o, int bitmask )
    {
      int i = ( int ) _o;
      return ( i & bitmask ) != 0;
    }

    /// <summary>
    /// Sets up the user roles from the "start" settings for a given group/role
    /// </summary>
    /// <param name="PageBoardID">Current BoardID</param>
    /// <param name="userName"></param>
    static public void SetupUserRoles( int pageBoardID, string userName )
    {
      using ( DataTable dt = YAF.Classes.Data.DB.group_list( pageBoardID, DBNull.Value ) )
      {
        foreach ( DataRow row in dt.Rows )
        {
          // see if the "Is Start" flag is set for this group and NOT the "Is Guest" flag (those roles aren't synced)
          if ( BitSet( row ["Flags"], 4 ) && !BitSet( row["Flags"], 2) )
          {
            // add the user to this role in membership
            string roleName = row ["Name"].ToString();
            Roles.AddUserToRole( userName, roleName );
          }
        }
      }
    }

    /// <summary>
    /// Syncs the ASP.NET roles with YAF groups bi-directionally.
    /// </summary>
    /// <param name="PageBoardID"></param>
    static public void SyncRoles( int PageBoardID )
    {
      // get all the groups in YAF DB and create them if they do not exist as a role in membership
      using ( DataTable dt = YAF.Classes.Data.DB.group_list( PageBoardID, DBNull.Value ) )
      {
        foreach ( DataRow row in dt.Rows )
        {
          string name = ( string ) row ["Name"];

          // bitset is testing if this role is a "Guest" role...
          // if it is, we aren't syncing it.
          if ( !BitSet(row["Flags"], 2) && !Roles.RoleExists( name ) )
          {
            Roles.CreateRole( name );
          }
        }

        // get all the roles and create them in the YAF DB if they do not exist
        foreach ( string role in Roles.GetAllRoles() )
        {
          int nGroupID = 0;
          string filter = string.Format( "Name='{0}'", role );
          DataRow [] rows = dt.Select( filter );

          if ( rows.Length == 0 )
          {
            // sets new roles to default "Read Only" access
            nGroupID = ( int ) YAF.Classes.Data.DB.group_save( DBNull.Value, PageBoardID, role, false, false, false, false, 1 );
          }
          else
          {
            nGroupID = ( int ) rows [0] ["GroupID"];
          }
        }
      }
    }

    /// <summary>
    /// Creates the user in the YAF DB from the ASP.NET Membership user information.
    /// Also copies the Roles as groups into YAF DB for the current user
    /// </summary>
    /// <param name="user">Current Membership User</param>
    /// <param name="pageBoardID">Current BoardID</param>
    /// <returns>Returns the UserID of the user if everything was successful. Otherwise, null.</returns>
    public static int? CreateForumUser( MembershipUser user, int pageBoardID )
    {
      int? userID = null;

      try
      {
        userID = YAF.Classes.Data.DB.user_aspnet( pageBoardID, user.UserName, user.Email, user.ProviderUserKey, user.IsApproved );

        foreach ( string role in Roles.GetRolesForUser( user.UserName ) )
        {
          YAF.Classes.Data.DB.user_setrole( pageBoardID, user.ProviderUserKey, role );
        }

        YAF.Classes.Data.DB.eventlog_create( DBNull.Value, user, string.Format( "Created forum user {0}", user.UserName ) );
      }
      catch ( Exception x )
      {
        YAF.Classes.Data.DB.eventlog_create( DBNull.Value, "CreateForumUser", x );
      }

      return userID;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="user"></param>
    /// <param name="pageBoardID"></param>
    /// <returns></returns>
    public static bool DidCreateForumUser( MembershipUser user, int pageBoardID )
    {
      int? userID = CreateForumUser( user, pageBoardID );
      return ( userID == null ) ? false : true;
    }

    /// <summary>
    /// Updates the information in the YAF DB from the ASP.NET Membership user information.
    /// Called once per session for a user to sync up the data
    /// </summary>
    /// <param name="user">Current Membership User</param>
    /// <param name="pageBoardID">Current BoardID</param>
    public static void UpdateForumUser( MembershipUser user, int pageBoardID )
    {
      int nUserID = YAF.Classes.Data.DB.user_aspnet( pageBoardID, user.UserName, user.Email, user.ProviderUserKey, user.IsApproved );
      YAF.Classes.Data.DB.user_setrole( pageBoardID, user.ProviderUserKey, DBNull.Value );
      foreach ( string role in Roles.GetRolesForUser( user.UserName ) )
      {
        YAF.Classes.Data.DB.user_setrole( pageBoardID, user.ProviderUserKey, role );
      }
    }
  }
}
