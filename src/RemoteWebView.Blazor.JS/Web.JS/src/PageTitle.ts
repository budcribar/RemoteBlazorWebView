import { getLogicalParent, LogicalElement } from './Rendering/LogicalElements';

export const PageTitle = {
  getAndRemoveExistingTitle,
};

function getAndRemoveExistingTitle(): string | null {
  const titleElements = document.getElementsByTagName('title');

  if (titleElements.length === 0) {
    return null;
  }

  let existingTitle: string | null = null;

  for (let index = titleElements.length - 1; index >= 0; index--) {
    const currentTitleElement = titleElements[index];
    const previousSibling = currentTitleElement.previousSibling;
    const isBlazorTitle = previousSibling instanceof Comment && getLogicalParent(previousSibling as unknown as LogicalElement) !== null;

    if (isBlazorTitle) {
      continue;
    }

    if (existingTitle === null) {
      existingTitle = currentTitleElement.textContent;
    }

    currentTitleElement.parentNode?.removeChild(currentTitleElement);
  }

  return existingTitle;
}
