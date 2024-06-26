﻿using BookShop.Server.Data;
using BookShop.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using SharedClassLibrary.Contracts;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using static BookShop.Shared.ServiceResponses;

namespace BookShop.Server;

public class AccountRepository(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, IConfiguration config) : IAccountRepository
{

    public async Task<GeneralResponse> CreateAccount(RegisterDTO registeredUser)
    {
        if (registeredUser is null) return new GeneralResponse(false, "Model is empty");
        var newUser = new ApplicationUser()
        {
            DisplayedName = registeredUser.DisplayedName,
            Email = registeredUser.Email,
            PasswordHash = registeredUser.Password,
            UserName = registeredUser.Email,
            PhoneNumber = registeredUser.PhoneNumber
        };
        var user = await userManager.FindByEmailAsync(newUser.Email);
        if (user is not null) return new GeneralResponse(false, "Existing_Email");

        var createUser = await userManager.CreateAsync(newUser!, registeredUser.Password);
        if (!createUser.Succeeded) return new GeneralResponse(false, "Error occured.. please try again", createUser.Errors);

        return await AssignUserToDefaultRole(newUser);
    }

    public async Task<LoginResponse> LoginAccount(LoginDTO loginDTO)
    {
        if (loginDTO == null)
            return new LoginResponse(false, null!, "Login container is empty");

        var getUser = await userManager.FindByEmailAsync(loginDTO.Email);
        if (getUser is null)
            return new LoginResponse(false, null!, "Invalid email/password");

        bool checkUserPasswords = await userManager.CheckPasswordAsync(getUser, loginDTO.Password);
        if (!checkUserPasswords)
            return new LoginResponse(false, null!, "Invalid email/password");

        var getUserRole = await userManager.GetRolesAsync(getUser);
        var userSession = new UserSession(getUser.Id, getUser.DisplayedName, getUser.Email, getUserRole.First(), getUser.PhoneNumber);
        string token = GenerateToken(userSession);
        return new LoginResponse(true, token!, "Login completed");
    }

    private string GenerateToken(UserSession user)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));

        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
        var userClaims = new[]
        {
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.DisplayedName),
            new Claim(ClaimTypes.MobilePhone,user.PhoneNumber)
        };
        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: userClaims,
            expires: DateTime.Now.AddDays(1),
            signingCredentials: credentials
            );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<GeneralResponse> UpdateUserData(RegisterDTO updatedUser)
    {
        var user = await userManager.FindByEmailAsync(updatedUser.Email);
        if (user is null)
            return new(false, "User not found");

        user.DisplayedName = updatedUser.DisplayedName;
        user.Email = updatedUser.Email;
        user.PasswordHash = userManager.PasswordHasher.HashPassword(user, updatedUser.Password);
        user.PhoneNumber = updatedUser.PhoneNumber;

        var response = await userManager.UpdateAsync(user);
        if (response.Succeeded)
            return new(true, "User data updated successfully") { Data = user };
        else
            return new(false, "Error while updating user data", response.Errors);
    }

    public async Task<GeneralResponse> AssignUserToDefaultRole(ApplicationUser newUser)
    {
        var checkAdmin = await roleManager.FindByNameAsync("Admin");
        if (checkAdmin is null)
            await roleManager.CreateAsync(new IdentityRole() { Name = ConstSettings.AdminRole });

        var usersNumber = userManager.Users.Count();
        if (usersNumber <= 1)
        {
            await userManager.AddToRoleAsync(newUser, ConstSettings.AdminRole);
            return new GeneralResponse(true, "Account Created");
        }
        else
        {
            var checkUser = await roleManager.FindByNameAsync(ConstSettings.DefalutRole);
            if (checkUser is null)
                await roleManager.CreateAsync(new IdentityRole() { Name = ConstSettings.DefalutRole });

            await userManager.AddToRoleAsync(newUser, "User");
            return new GeneralResponse(true, "Account Created");
        }
    }
}
