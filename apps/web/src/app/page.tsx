import { Button } from "@st/ui/components/button";
import { ThemeToggle } from "@/components/theme-toggle";

// Temporary scaffolding: proves the token pipeline and both themes render.
// Delete once the first real screen lands.

const semantic = [
  { name: "primary", bg: "bg-primary", fg: "text-primary-foreground" },
  { name: "success", bg: "bg-success", fg: "text-success-foreground" },
  { name: "warning", bg: "bg-warning", fg: "text-warning-foreground" },
  { name: "destructive", bg: "bg-destructive", fg: "text-white" },
];

export default function Page() {
  return (
    <main className="mx-auto flex max-w-2xl flex-col gap-10 p-10">
      <header className="flex items-center justify-between">
        <div>
          <h1 className="font-heading text-2xl font-semibold">Palette check</h1>
          <p className="text-muted-foreground text-sm">
            Brand is indigo; success, warning and destructive stay distinct.
          </p>
        </div>
        <ThemeToggle />
      </header>

      <section className="flex flex-wrap gap-2">
        <Button>Primary</Button>
        <Button variant="secondary">Secondary</Button>
        <Button variant="outline">Outline</Button>
        <Button variant="ghost">Ghost</Button>
        <Button variant="destructive">Destructive</Button>
        <Button variant="link">Link</Button>
      </section>

      <section className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        {semantic.map((token) => (
          <div
            key={token.name}
            className={`${token.bg} ${token.fg} rounded-lg p-4 text-sm font-medium`}
          >
            {token.name}
          </div>
        ))}
      </section>
    </main>
  );
}
