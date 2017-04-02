﻿using System;
using System.Collections.Generic;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.DataStructures.Geometric;

namespace Sledge.BspEditor.Primitives.MapObjects
{
    public class Entity : BaseMapObject, IPrimitive
    {
        public EntityData EntityData => Data.GetOne<EntityData>();
        public ObjectColor Color => Data.GetOne<ObjectColor>();
        public Coordinate Origin { get; set; }

        public Entity(long id) : base(id)
        {
        }

        public override void DescendantsChanged()
        {
            Hierarchy.Parent?.DescendantsChanged();
        }

        public override IMapObject Clone()
        {
            var ent = new Entity(ID);
            CloneBase(ent);
            ent.Origin = Origin.Clone();
            return ent;
        }

        public override void Unclone(IMapObject obj)
        {
            if (!(obj is Entity)) throw new ArgumentException("Cannot unclone into a different type.", nameof(obj));
            UncloneBase((BaseMapObject)obj);
        }

        public override IEnumerable<IPrimitive> ToPrimitives()
        {
            yield return this;
        }
    }
}