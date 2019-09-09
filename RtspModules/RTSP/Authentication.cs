using NLog;
using System;
using System.Security.Cryptography;
using System.Text;

namespace Rtsp
{
    public enum AuthenticationType {Basic, Digest};

    public class AuthenticationParameters
    {
        public String username = null;
        public String password = null;
        public String realm = null;
        public String nonce = null;
        public AuthenticationType authenticationType = AuthenticationType.Digest;
    }

    // WWW-Authentication and Authorization Headers
    public class Authentication
    {
        private String _username = null;
        private String _password = null;
        private String _realm = null;
        private String _nonce = null;
        private AuthenticationType _authenticationType = AuthenticationType.Digest;
        private readonly MD5 _md5 = System.Security.Cryptography.MD5.Create();
        private readonly ILogger _logger;

        private const char quote = '\"';

        // Constructor
        public Authentication(AuthenticationParameters authenticationParameters, ILogger logger) {
            _logger = logger;

            this._username = authenticationParameters.username;
            this._password = authenticationParameters.password;
            this._realm = authenticationParameters.realm;
            this._authenticationType = authenticationParameters.authenticationType;

            this._nonce = new Random().Next(100000000,999999999).ToString(); // random 9 digit number            
        }

        public String GetHeader() {
            if (_authenticationType == AuthenticationType.Basic) {
                return "Basic realm=" + quote + _realm + quote;
            }
            if (_authenticationType == AuthenticationType.Digest) {
                return "Digest realm=" + quote + _realm + quote + ", nonce=" + quote + _nonce + quote;
            }
            return null;
        }
        

		public bool IsValid(Rtsp.Messages.RtspMessage receivedMessage) {
			
			string authorization = receivedMessage.Headers["Authorization"];
            

			// Check Username and Password
            if (_authenticationType == AuthenticationType.Basic && authorization.StartsWith("Basic ")) {
                string base64Str = authorization.Substring(6); // remove 'Basic '
                byte[] data = Convert.FromBase64String(base64Str);
                string decoded = Encoding.UTF8.GetString(data);
                int splitPosition = decoded.IndexOf(':');
                string decodedUsername = decoded.Substring(0, splitPosition);
                string decodedPassword = decoded.Substring(splitPosition + 1);

                if ((decodedUsername == _username) && (decodedPassword == _password)) {
					_logger.Debug("Basic Authorization passed");
                    return true;
                } else {
					_logger.Debug("Basic Authorization failed");
                    return false;
                }
            }

            // Check Username, URI, Nonce and the MD5 hashed Response
            if (_authenticationType == AuthenticationType.Digest && authorization.StartsWith("Digest ")) {
                string valueStr = authorization.Substring(7); // remove 'Digest '
                string[] values = valueStr.Split(',');
                string authHeaderUsername = null;
				string authHeaderRealm = null;
                string authHeaderNonce = null;
				string authHeaderUri = null;
                string authHeaderResponse = null;
				string messageMethod = null;
				string messageUri = null;
				try {
					messageMethod = receivedMessage.Command.Split(' ')[0];
                    messageUri = receivedMessage.Command.Split(' ')[1];
				} catch {}

                foreach (string value in values) {
                    string[] tuple = value.Trim().Split(new char[] {'='},2); // split on first '=' 
                    if (tuple.Length == 2 && tuple[0].Equals("username")) {
                        authHeaderUsername = tuple[1].Trim(new char[] {' ','\"'}); // trim space and quotes
                    }
					else if (tuple.Length == 2 && tuple[0].Equals("realm")) {
                        authHeaderRealm = tuple[1].Trim(new char[] {' ','\"'}); // trim space and quotes
                    }
                    else if (tuple.Length == 2 && tuple[0].Equals("nonce")) {
						authHeaderNonce = tuple[1].Trim(new char[] {' ','\"'}); // trim space and quotes
                    }
                    else if (tuple.Length == 2 && tuple[0].Equals("uri")) {
						authHeaderUri = tuple[1].Trim(new char[] {' ','\"'}); // trim space and quotes
                    }
                    else if (tuple.Length == 2 && tuple[0].Equals("response")) {
						authHeaderResponse = tuple[1].Trim(new char[] {' ','\"'}); // trim space and quotes
                    }
                }

                // Create the MD5 Hash using all parameters passed in the Auth Header with the 
                // addition of the 'Password'
				String hashA1 = CalculateMD5Hash(_md5, authHeaderUsername+":"+authHeaderRealm+":"+this._password);
                String hashA2 = CalculateMD5Hash(_md5, messageMethod + ":" + authHeaderUri);
                String expectedResponse = CalculateMD5Hash(_md5, hashA1 + ":" + authHeaderNonce + ":" + hashA2);

                // Check if everything matches
				// ToDo - extract paths from the URIs (ignoring SETUP's trackID)
				if ((authHeaderUsername == this._username)
				    && (authHeaderRealm == this._realm)
				    && (authHeaderNonce == this._nonce)
				    && (authHeaderResponse == expectedResponse)
				   ){
				    _logger.Debug("Digest Authorization passed");
                    return true;
                } else {
					_logger.Debug("Digest Authorization failed");
                    return false;
                }
            }
            return false;
        }



        // Generate Basic or Digest Authorization
        public string GenerateAuthorization(string username, string password,
                                            string authType, string realm, string nonce, string url, string command)  {

            if (username == null || username.Length == 0) return null;
            if (password == null || password.Length == 0) return null;
            if (realm == null || realm.Length == 0) return null;
            if (authType.Equals("Digest") && (nonce == null || nonce.Length == 0)) return null;

            if (authType.Equals("Basic")) {
                byte[] credentials = System.Text.Encoding.UTF8.GetBytes(username+":"+password);
                String credentialsBase64 = Convert.ToBase64String(credentials);
                String basicAuthorization = "Basic " + credentialsBase64;
                return basicAuthorization;
            }
            else if (authType.Equals("Digest")) {

                MD5 md5 = System.Security.Cryptography.MD5.Create();
                String hashA1 = CalculateMD5Hash(md5, username+":"+realm+":"+password);
                String hashA2 = CalculateMD5Hash(md5, command + ":" + url);
                String response = CalculateMD5Hash(md5, hashA1 + ":" + nonce + ":" + hashA2);

                const String quote = "\"";
                String digestAuthorization = "Digest username=" + quote + username + quote +", "
                    + "realm=" + quote + realm + quote + ", "
                    + "nonce=" + quote + nonce + quote + ", "
                    + "uri=" + quote + url + quote + ", "
                    + "response=" + quote + response + quote;

                return digestAuthorization;
            }
            else {
                return null;
            }

        }
        


        // MD5 (lower case)
        private string CalculateMD5Hash(MD5 md5Session, string input)
        {
            byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
            byte[] hash = md5Session.ComputeHash(inputBytes);

            StringBuilder output = new StringBuilder();
            for (int i = 0; i < hash.Length; i++) {
                output.Append(hash[i].ToString("x2"));
            }

            return output.ToString();
        }
    }
}