import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";
import NotFound from "../not-found";

test("NotFound renders a heading and a link home", () => {
  render(<NotFound />);
  expect(
    screen.getByRole("heading", { level: 1, name: "Page not found" }),
  ).toBeDefined();
  expect(screen.getByRole("link", { name: "Go home" })).toBeDefined();
});
