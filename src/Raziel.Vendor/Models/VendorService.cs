﻿using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Raziel.Library.Classes;
using Raziel.Library.Classes.Crypto;
using Raziel.Library.Models;

namespace Raziel.Vendor.Models {
    public class VendorService : IVendorService {
        private readonly RazielContext _context;
        private readonly ILogger _logger;
        private readonly IMemoryCache _memoryCache;
        private readonly Settings _settings;

        public VendorService(RazielContext context, Settings settings, IMemoryCache memoryCache, ILoggerFactory logger) {
            _context = context;
            _settings = settings;
            _memoryCache = memoryCache;
            _logger = logger.CreateLogger("Vendor");
        }

        public AuthenticationRequest GenerateToken(AuthenticationRequest request) {
            var user = FetchUser(request.User.Username);
            if (user == null) return null;

            // Encrypt the token with the end users public key. If they're able to decrypt it we know they're valid
            request.Token = Cryptide.Instance.Encrypt(GenerateToken(request.User.Username), user.VendorPublicKey);

            _logger.LogMsg("Created token for user", new AuthenticationModel {Username = request.User.Username});
            return request;
        }

        public User GetDetails(AuthenticationRequest request) {
            _logger.LogMsg("Returned details for user", new AuthenticationModel {Username = request.User.Username});
            return FetchUser(request.User.Username);
        }

        public bool Save(AuthenticationRequest request) {
            try {
                var user = FetchUser(request.User.Username);
                if (user == null) return false;

                user.BitcoinPrivateKey = request.User.BitcoinPrivateKey;
                user.FirstName = request.User.FirstName;
                user.LastName = request.User.LastName;
                user.Note = request.User.Note;

                _logger.LogMsg("Updated details for user", new AuthenticationModel {Username = request.User.Username});
                _context.SaveChanges();
                return true;
            }
            catch (Exception) {
                return false;
            }
        }

        private User FetchUser(string username) {
            // Fetch the item from the cache, otherwise get it from the blockchain
            if (_memoryCache.GetCacheObject(CacheKeys.VendorUser, out User user, 0, username)) return user;
            user = _context.Users.FirstOrDefault(u => u.Username == username);
            if (user != null) _memoryCache.SetCacheObject(CacheKeys.VendorUser, user);

            return user;
        }

        private string GenerateToken(string publicKey) {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_settings.Password);
            var tokenDescriptor = new SecurityTokenDescriptor {
                Subject = new ClaimsIdentity(new[] {
                    new Claim(ClaimTypes.Name, publicKey)
                }),
                Expires = DateTime.UtcNow.AddHours(5),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }
}