//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated from a template.
//
//     Manual changes to this file may cause unexpected behavior in your application.
//     Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace api
{
    using System;
    using System.Collections.Generic;
    
    public partial class User
    {
        public int Id { get; set; }
        public string username { get; set; }
        public string password { get; set; }
        public byte[] profile_image { get; set; }
        public string unique_id { get; set; }
        public string user_display_name { get; set; }
        public Nullable<System.DateTime> profile_created { get; set; }
        public Nullable<System.DateTime> last_logon { get; set; }
        public Nullable<System.DateTime> user_birthday { get; set; }
    }
}
