﻿using Autofac;

namespace Smartstore.Engine.DependencyInjection
{
    public class WorkModule : Autofac.Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterGeneric(typeof(WorkValues<>)).InstancePerLifetimeScope();
            builder.RegisterSource(new WorkSource());
        }
    }
}
