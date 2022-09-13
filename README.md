# xxlib_codegen
code gen tools source code for xxlib ( c++, c#, lua and more )

# config file example

## template files
```
abc.cs
def.cs
```

## gen_cfg.json content
```
{
	"refs":[]
	"name":"Shared"								// maybe used by root namespace or struct name prefix
	"files":[".\abc.cs", ".\def.cs"],			// combile compile to IL's file list. path relative for gen_cfg.json

	"outdir_cs":"..\out\cs\"					// gen to cs ( if not exists, mean not gen )
	"outdir_lua":"..\out\lua\"					// gen to lua
	"outdir_cpp":"..\out\cpp\"					// gen to cpp
	"outdir_rs":"..\out\rs\"					// gen to rust
	"outdir_js":"..\out\js\"					// gen to js

    "typeid_from": 1,                           // [TypeId( value from )]. default min type id from = 0 ( for verify )
    "typeid_to": 99                             // [TypeId( value to )]. default max type id = 65536
}
```

## template files
```
efg.cs
hhhh.cs
```

## gen_cfg.json content
```
{
	"refs":["..\..\others\gen_cfg.json", ......],		// depend template config. external include
	"name":"P1",
	"files":[".\efg.cs", ".\hhhh.cs"],
	"outdir_cs":"..\..\..\out\cs\"
	"outdir_lua":"..\..\..\out\lua\"
	"outdir_cpp":"..\..\..\out\cpp\"
    "outdir_rs":"..\..\..\out\rs\"
    "outdir_js":"..\..\..\out\js\"
}
```

## command line generate code example:

```
????\xxlib_codegen\codegen_src\bin\Debug\net6.0\codegen_src.exe ?????\gen_cfg.json
```
