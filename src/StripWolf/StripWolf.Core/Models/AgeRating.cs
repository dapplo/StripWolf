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

using System.Xml.Serialization;

namespace StripWolf.Core.Models;

/// <summary>
/// Age rating enumeration
/// </summary>
public enum AgeRating
{
    [XmlEnum("Unknown")]
    Unknown,
    [XmlEnum("Adults Only 18+")]
    AdultsOnly18Plus,
    [XmlEnum("Early Childhood")]
    EarlyChildhood,
    [XmlEnum("Everyone")]
    Everyone,
    [XmlEnum("Everyone 10+")]
    Everyone10Plus,
    [XmlEnum("G")]
    G,
    [XmlEnum("Kids to Adults")]
    KidsToAdults,
    [XmlEnum("M")]
    M,
    [XmlEnum("MA15+")]
    MA15Plus,
    [XmlEnum("Mature 17+")]
    Mature17Plus,
    [XmlEnum("PG")]
    PG,
    [XmlEnum("R18+")]
    R18Plus,
    [XmlEnum("Rating Pending")]
    RatingPending,
    [XmlEnum("Teen")]
    Teen,
    [XmlEnum("X18+")]
    X18Plus
}

