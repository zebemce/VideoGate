using System;
using System.Linq;
using System.Net;

namespace Rtsp
{
    public static class IPUtils
    {

        public static IPAddress GetIPAddressFromString(string address, out Exception exception)
        {
            IPAddress result;
            exception = null;
            
            if (string.IsNullOrEmpty(address)  || 
                false == System.Net.IPAddress.TryParse(address, out result))
            {
                try
                {
                    result = System.Net.Dns.GetHostEntry(address).AddressList.Last(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                }
                catch(Exception ex)
                {
                    exception = ex;

                    result = IPAddress.Any;
                }
            }

            return result;
        }
    }
}
