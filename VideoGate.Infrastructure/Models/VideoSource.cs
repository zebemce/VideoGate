using System;
using System.Runtime.Serialization;

namespace VideoGate.Infrastructure.Models
{
    [DataContract]
    public class VideoSource
    {
        [DataMember(Name = "id",Order = 0)]
        public Guid Id {get; set;}
        [DataMember(Name = "caption",Order = 1)]
        public string Caption {get; set;}
        [DataMember(Name = "url",Order = 2)]
        public string Url {get; set;}
        [DataMember(Name = "enabled",Order = 3)]
        public bool Enabled {get; set;}
        [DataMember(Name = "useTCP",Order = 4)]
        public bool UseTCP {get; set;}
        [DataMember(Name = "timeout",Order = 5)]
        public int Timeout {get; set;} = 0;
        
    }
}
