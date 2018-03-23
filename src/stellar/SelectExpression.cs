﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq.Expressions;

namespace Stellar
{
    internal class SelectExpression : Expression
    {
        internal SelectExpression(Type type, string alias, IEnumerable<ColumnDeclaration> columns, Expression from, Expression where) : base((ExpressionType)DbExpressionType.Select, type)
        {
            Alias = alias;
            From = from;
            Where = where;
            Columns = columns as ReadOnlyCollection<ColumnDeclaration>;
            if (Columns == null)
                Columns = new List<ColumnDeclaration>(columns).AsReadOnly();
        }

        internal string Alias { get; private set; }
        internal Expression From { get; private set; }
        internal Expression Where { get; private set; }
        internal ReadOnlyCollection<ColumnDeclaration> Columns { get; private set; }

    }
}
