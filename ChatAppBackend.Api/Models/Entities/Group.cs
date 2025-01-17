﻿using MongoDB.Bson;
using WebService.Attributes;

namespace WebService.Models.Entities;

[BsonCollection("groups")]
public class Group : Document
{
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public ICollection<ObjectId> UserIds { get; set; } = new List<ObjectId>();
}