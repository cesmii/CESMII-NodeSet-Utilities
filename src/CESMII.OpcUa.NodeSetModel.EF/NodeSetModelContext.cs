using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace CESMII.OpcUa.NodeSetModel.EF
{
    public class NodeSetModelContext : DbContext
    {
        public NodeSetModelContext(DbContextOptions<NodeSetModelContext> options) : base(options)
        {
            // Blank
        }

        protected NodeSetModelContext(DbContextOptions options)
        {
            // Blank
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder
                .UseLazyLoadingProxies()
            ;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            CreateModel(modelBuilder);

        }
        public static void CreateModel(ModelBuilder modelBuilder)
        {
            modelBuilder.Owned<NodeModel.LocalizedText>();
            modelBuilder.Owned<NodeModel.NodeAndReference>();
            modelBuilder.Owned<VariableModel.EngineeringUnitInfo>();
            modelBuilder.Owned<DataTypeModel.StructureField>();
            modelBuilder.Owned<DataTypeModel.UaEnumField>();
            modelBuilder.Owned<RequiredModelInfo>();

            modelBuilder.Entity<NodeSetModel>()
                .ToTable("NodeSets")
                .Ignore(nsm => nsm.AllNodesByNodeId)
                .Ignore(nsm => nsm.CustomState)
                .HasKey(nsm => new { nsm.ModelUri, nsm.PublicationDate })
                ;
            modelBuilder.Entity<NodeSetModel>()
                .OwnsMany(nsm => nsm.RequiredModels).WithOwner()
                    .HasForeignKey("DependentModelUri", "DependentPublicationDate")
                ;
            modelBuilder.Entity<NodeModel>()
                .Ignore(nm => nm.CustomState)
                .Ignore(nsm => nsm.OtherReferencingNodes) // Populated from nsm.OtherChildren in the NodeSetModel factories
                .Property<DateTime?>("NodeSetPublicationDate") // EF tooling does not properly infer the type of this auto-generated property when using it in a foreign key: workaround declare explcitly
                ;
            modelBuilder.Entity<NodeModel>()
                .ToTable("Nodes")
                // This syntax is not supported by EF: use without typing
                //.HasKey(nm => new { nm.NodeId, nm.NodeSet.ModelUri, nm.NodeSet.PublicationDate })
                .HasKey(
                    nameof(NodeModel.NodeId),
                    $"{nameof(NodeModel.NodeSet)}{nameof(NodeSetModel.ModelUri)}",// Foreign key with auto-generated PK of the NodeModel.NodeSet property
                    $"{nameof(NodeModel.NodeSet)}{nameof(NodeSetModel.PublicationDate)}")
                ;
            modelBuilder.Entity<ObjectTypeModel>()
                .ToTable("ObjectTypes")
                ;
            modelBuilder.Entity<DataTypeModel>()
                .ToTable("DataTypes")
                ;
            modelBuilder.Entity<VariableTypeModel>()
                .ToTable("VariableTypes")
                ;
            modelBuilder.Entity<DataVariableModel>()
                .ToTable("DataVariables")
                .HasOne(dv => dv.Parent).WithMany()
                    .HasForeignKey("ParentNodeId", "ParentModelUri", "ParentPublicationDate")
                ;
            modelBuilder.Entity<PropertyModel>()
                .ToTable("Properties")
                .HasOne(dv => dv.Parent).WithMany()
                    .HasForeignKey("ParentNodeId", "ParentModelUri", "ParentPublicationDate")
                ;
            modelBuilder.Entity<ObjectModel>()
                .ToTable("Objects")
                .HasOne<ObjectTypeModel>(o => o.TypeDefinition).WithMany()
                ;
            modelBuilder.Entity<ObjectModel>()
                .HasOne(dv => dv.Parent).WithMany()
                    .HasForeignKey("ParentNodeId", "ParentModelUri", "ParentPublicationDate")
                ;
            modelBuilder.Entity<InterfaceModel>()
                .ToTable("Interfaces")
                ;
            modelBuilder.Entity<VariableModel>()
                .ToTable("Variables")
                .OwnsOne(v => v.EngineeringUnit).Property(v => v.NamespaceUri).IsRequired()
                ;
            modelBuilder.Entity<BaseTypeModel>()
                .ToTable("BaseTypes")
                .Ignore(m => m.SubTypes)
                ;
            modelBuilder.Entity<MethodModel>()
                .ToTable("Methods")
                .HasOne(dv => dv.Parent).WithMany()
                    .HasForeignKey("ParentNodeId", "ParentModelUri", "ParentPublicationDate")
                ;
            modelBuilder.Entity<ReferenceTypeModel>()
                .ToTable("ReferenceTypes")
                ;

            #region NodeSetModel collections
            DeclareNodeSetCollection<ObjectTypeModel>(modelBuilder, nsm => nsm.ObjectTypes);
            DeclareNodeSetCollection<VariableTypeModel>(modelBuilder, nsm => nsm.VariableTypes);
            DeclareNodeSetCollection<DataTypeModel>(modelBuilder, nsm => nsm.DataTypes);
            DeclareNodeSetCollection<ReferenceTypeModel>(modelBuilder, nsm => nsm.ReferenceTypes);
            DeclareNodeSetCollection<ObjectModel>(modelBuilder, nsm => nsm.Objects);
            //DeclareNodeSetCollection<BaseTypeModel>(modelBuilder, nsm => nsm.Interfaces);
            DeclareNodeSetCollection<InterfaceModel>(modelBuilder, nsm => nsm.Interfaces);
            DeclareNodeSetCollection<PropertyModel>(modelBuilder, nsm => nsm.Properties);
            DeclareNodeSetCollection<DataVariableModel>(modelBuilder, nsm => nsm.DataVariables);
            DeclareNodeSetCollection<NodeModel>(modelBuilder, nsm => nsm.UnknownNodes);
            #endregion

            #region NodeModel collections
            // Unclear why these collection require declarations while the others just work
            modelBuilder.Entity<DataVariableModel>()
                .HasMany(dv => dv.NodesWithDataVariables).WithMany(nm => nm.DataVariables);
            modelBuilder.Entity<NodeModel>()
                .HasMany(nm => nm.Properties).WithMany(v => v.NodesWithProperties);
            modelBuilder.Entity<NodeModel>()
                .HasMany(nm => nm.Interfaces).WithMany(v => v.NodesWithInterface);

            #endregion

            var orn = modelBuilder.Entity<NodeModel>()
                .OwnsMany<NodeModel.NodeAndReference>(nm => nm.OtherReferencedNodes)
                ;
            orn.WithOwner()
                .HasForeignKey("OwnerNodeId", "OwnerModelUri", "OwnerPublicationDate")
                ;
            orn.Property<string>("ReferencedNodeId");
            orn.Property<string>("ReferencedModelUri");
            orn.Property<DateTime?>("ReferencedPublicationDate");
            orn.HasOne(nr => nr.Node).WithMany()
                .HasForeignKey("ReferencedNodeId", "ReferencedModelUri", "ReferencedPublicationDate")
                ;
            orn.Property<string>("OwnerNodeId");
            orn.Property<string>("OwnerModelUri");
            orn.Property<DateTime?>("OwnerPublicationDate");
        }

        private static void DeclareNodeSetCollection<TEntity>(ModelBuilder modelBuilder, Expression<Func<NodeSetModel, IEnumerable<TEntity>>> collection) where TEntity : NodeModel
        {
            var collectionName = (collection.Body as MemberExpression).Member.Name;
            var modelProp = $"NodeSet{collectionName}ModelUri";
            var pubDateProp = $"NodeSet{collectionName}PublicationDate";
            modelBuilder.Entity<TEntity>().Property<string>(modelProp);
            modelBuilder.Entity<TEntity>().Property<DateTime?>(pubDateProp);
            modelBuilder.Entity<TEntity>().HasOne("CESMII.OpcUa.NodeSetModel.NodeSetModel", null)
                .WithMany(collectionName)
                .HasForeignKey(modelProp, pubDateProp);
            // With this typed declaration the custom property names are not picked up for some reason
            //modelBuilder.Entity<TEntity>()
            //    .HasOne(nm => nm.NodeSet).WithMany(collection)
            //        .HasForeignKey(modelProp, pubDateProp)
            //        ;
            //modelBuilder.Entity<NodeSetModel>()
            //    .HasMany(collection).WithOne(nm => nm.NodeSet)
            //        .HasForeignKey(modelProp, pubDateProp)
            //    ;
        }

        public DbSet<NodeSetModel> NodeSets { get; set; }
        public DbSet<NodeModel> NodeModels { get; set; }
    }

}