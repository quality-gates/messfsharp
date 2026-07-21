# Custom rulesets

The positional ruleset argument accepts a path to an XML file. A ruleset can
reference a complete built-in catalog or one named rule:

```xml
<?xml version="1.0" encoding="utf-8"?>
<ruleset name="repository">
  <rule ref="rulesets/codesize.xml">
    <exclude name="ExcessiveClassLength" />
    <priority>2</priority>
    <properties>
      <property name="maximum" value="8" />
    </properties>
  </rule>
  <rule ref="rulesets/naming.xml/LongVariable">
    <properties>
      <property name="maximum" value="30" />
    </properties>
  </rule>
</ruleset>
```

`rule name="CyclomaticComplexity"` is the short form for a single built-in
rule. Complete ruleset references may also appear as nested `ruleset` elements.
Rule names are exact after case-insensitive lookup. Loaded rules are
deduplicated by rule identity; later explicit references provide the final
priority and property override. Unknown files are operational errors. Unknown
references are retained as verbose diagnostics and are never replaced by an
unrelated implementation.
