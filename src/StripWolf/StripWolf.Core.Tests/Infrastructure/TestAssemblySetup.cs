// StripWolf - an open source comic book reader
// Copyright (C) 2026 Dapplo - Robin Krom
//
// For more information see: https://github.com/dapplo/StripWolf
// The StripWolf project is hosted on GitHub https://github.com/dapplo/StripWolf
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Runtime.CompilerServices;
using StripWolf.Core.Services;

namespace StripWolf.Core.Tests;

internal static class TestAssemblySetup
{
    /// <summary>
    /// Runs once when the test assembly is loaded: keep the application log out of the real user data directory.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        Logger.RedirectTo(Path.Combine(Path.GetTempPath(), "StripWolf.Tests", $"stripwolf-{Environment.ProcessId}.log"));
    }
}
